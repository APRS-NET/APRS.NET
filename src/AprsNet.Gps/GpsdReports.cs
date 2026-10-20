using System.Globalization;
using System.Text.Json;
using AprsNet.Packets;

namespace AprsNet.Gps;

/// <summary>
/// Reads gpsd's JSON reports, one per line (gpsd_json(5)): a fix from each TPV report, with the satellites and HDOP of
/// the latest SKY report, and gpsd's version. Lines that aren't JSON, and other classes, are skipped.
/// </summary>
internal sealed class GpsdReports
{
    private const double MetresPerSecondPerKnot = 1852.0 / 3600;
    private int? _satellites;
    private double? _hdop;

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
    }

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
