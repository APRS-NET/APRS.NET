using System.Globalization;
using System.Text.Json;
using AprsNet.Packets;

namespace AprsNet.Gps;

/// <summary>
/// Reads gpsd's JSON reports, one per line (gpsd_json(5)): a fix from each TPV report, with the satellites and HDOP of
/// the latest SKY report; the fix's quality and the satellites in view from SKY and TPV; and gpsd's version. Lines
/// that aren't JSON, and other classes, are skipped.
/// </summary>
internal sealed class GpsdReports
{
    private const double MetresPerSecondPerKnot = 1852.0 / 3600;
    private int? _satellites;
    private double? _hdop;

    /// <summary>The quality of the fix: DOPs and counts from the latest SKY report, errors from the latest TPV.</summary>
    public GpsQuality Quality { get; private set; } = new();

    /// <summary>The satellites of the latest SKY report that listed them; replaced, never changed.</summary>
    public IReadOnlyList<GpsSatellite> Satellites { get; private set; } = [];

    /// <summary>gpsd's version, from its VERSION report: <c>3.25</c>.</summary>
    public string? Release { get; private set; }

    /// <summary>The error gpsd last reported, from an ERROR report: an unknown device, say.</summary>
    public string? LastError { get; private set; }

    /// <summary>The command that asks gpsd for JSON reports, from every GPS or from <paramref name="device"/>.</summary>
    public static string Watch(string? device) =>
        device is null
            ? "?WATCH={\"enable\":true,\"json\":true}\n"
            : $"?WATCH={{\"enable\":true,\"json\":true,\"device\":{JsonSerializer.Serialize(device)}}}\n";

    /// <summary>Reads one line; returns the fix when it is a TPV report.</summary>
    public GpsFix? Read(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var report = document.RootElement;
            if (report.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            switch (Text(report, "class"))
            {
                case "VERSION":
                    Release = Text(report, "release");
                    return null;
                case "ERROR":
                    LastError = Text(report, "message");
                    return null;
                case "SKY":
                    Sky(report);
                    return null;
                case "TPV":
                    return Tpv(report);
                default:
                    return null;
            }
        }
    }

    private void Sky(JsonElement sky)
    {
        // gpsd 3.20 and later count the satellites used (uSat); older ones list them, each marked used or not.
        _satellites =
            Int(sky, "uSat")
            ?? (
                sky.TryGetProperty("satellites", out var satellites) && satellites.ValueKind == JsonValueKind.Array
                    ? satellites
                        .EnumerateArray()
                        .Count(s => s.TryGetProperty("used", out var used) && used.ValueKind == JsonValueKind.True)
                    : _satellites
            );
        _hdop = Number(sky, "hdop") ?? _hdop;

        // Some GPSs send their DOPs in a SKY report of their own, without the satellites: keep the last list.
        if (sky.TryGetProperty("satellites", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            Satellites =
            [
                .. list.EnumerateArray()
                    .Where(s => s.ValueKind == JsonValueKind.Object && Int(s, "PRN") is not null)
                    .Select(s => new GpsSatellite
                    {
                        System = SystemOf(Int(s, "gnssid"), Int(s, "PRN")!.Value),
                        Id = Int(s, "PRN")!.Value,
                        SignalId = Int(s, "sigid"),
                        ElevationDegrees = Number(s, "el"),
                        AzimuthDegrees = Number(s, "az"),
                        SnrDbHz = Number(s, "ss") is { } ss and > 0 ? ss : null, // 0 when not tracked
                        Used = s.TryGetProperty("used", out var used) && used.ValueKind == JsonValueKind.True,
                    }),
            ];
        }

        Quality = Quality with
        {
            SatellitesUsed = _satellites,
            SatellitesInView =
                Int(sky, "nSat")
                ?? (
                    sky.TryGetProperty("satellites", out _)
                        ? Satellites.Select(s => (s.System, s.Id)).Distinct().Count()
                        : Quality.SatellitesInView
                ),
            Pdop = Number(sky, "pdop") ?? Quality.Pdop,
            Hdop = Number(sky, "hdop") ?? Quality.Hdop,
            Vdop = Number(sky, "vdop") ?? Quality.Vdop,
            Tdop = Number(sky, "tdop") ?? Quality.Tdop,
            Gdop = Number(sky, "gdop") ?? Quality.Gdop,
        };
    }

    /// <summary>
    /// A satellite's system from gpsd's <c>gnssid</c>, or when it's missing (gpsd before 3.18) from its PRN.
    /// </summary>
    private static GnssSystem SystemOf(int? gnssid, int prn) =>
        gnssid switch
        {
            0 => GnssSystem.Gps,
            1 => GnssSystem.Sbas,
            2 => GnssSystem.Galileo,
            3 => GnssSystem.BeiDou,
            5 => GnssSystem.Qzss,
            6 => GnssSystem.Glonass,
            7 => GnssSystem.NavIC,
            null => prn switch
            {
                >= 1 and <= 32 => GnssSystem.Gps,
                (>= 33 and <= 64) or (>= 120 and <= 158) => GnssSystem.Sbas,
                >= 65 and <= 96 => GnssSystem.Glonass,
                >= 193 and <= 200 => GnssSystem.Qzss,
                >= 201 and <= 263 => GnssSystem.BeiDou,
                >= 301 and <= 336 => GnssSystem.Galileo,
                _ => GnssSystem.Unknown,
            },
            _ => GnssSystem.Unknown, // 4, IMES
        };

    private GpsFix Tpv(JsonElement tpv)
    {
        var mode = Int(tpv, "mode") switch
        {
            1 => GpsFixMode.NoFix,
            2 => GpsFixMode.Fix2D,
            3 => GpsFixMode.Fix3D,
            _ => GpsFixMode.Unknown,
        };
        var hasFix = mode is GpsFixMode.Fix2D or GpsFixMode.Fix3D;
        // gpsd leaves the estimates out when it can't make them, so a missing one is unknown, not the last one.
        Quality = Quality with
        {
            HorizontalErrorMeters = Number(tpv, "eph"),
            LatitudeErrorMeters = Number(tpv, "epy"),
            LongitudeErrorMeters = Number(tpv, "epx"),
            AltitudeErrorMeters = Number(tpv, "epv"),
            SpeedErrorKnots = Number(tpv, "eps") / MetresPerSecondPerKnot,
        };
        var time =
            Text(tpv, "time") is { } text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
                ? parsed
                : (DateTimeOffset?)null;
        return new GpsFix
        {
            Mode = mode,
            Position =
                hasFix
                && Number(tpv, "lat") is { } lat and >= -90 and <= 90
                && Number(tpv, "lon") is { } lon and >= -180 and <= 180
                    ? new AprsPosition(lat, lon)
                    : null,
            UtcDate = time is { } t ? DateOnly.FromDateTime(t.UtcDateTime) : null,
            UtcTime = time is { } u ? TimeOnly.FromDateTime(u.UtcDateTime) : null,
            SpeedKnots = Number(tpv, "speed") / MetresPerSecondPerKnot,
            Course = Number(tpv, "track"),
            // gpsd 3.20 and later give altMSL; before, alt was the altitude above mean sea level.
            AltitudeMeters = mode == GpsFixMode.Fix3D ? Number(tpv, "altMSL") ?? Number(tpv, "alt") : null,
            Satellites = _satellites,
            Hdop = _hdop,
        };
    }

    private static string? Text(JsonElement report, string name) =>
        report.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement report, string name) =>
        report.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
        && double.IsFinite(number)
            ? number
            : null;

    private static int? Int(JsonElement report, string name) =>
        report.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
