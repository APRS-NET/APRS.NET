using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// Puts a GPS's NMEA sentences together into one <see cref="GpsFix"/>. A GPS sends several sentences each second (RMC
/// with the date and speed, GGA with the altitude, GSA with the 2D or 3D fix); hand each one to <see cref="Update"/> and
/// read <see cref="Fix"/>. For a GPS status view it also gathers <see cref="Quality"/> (from GGA, GSA and GST) and the
/// <see cref="Satellites"/> in view (from GSV, marked used from GSA).
/// </summary>
/// <remarks>
/// <para>
/// A sentence with a new time of day starts a new fix, so values from an older second never mix with a newer one.
/// Sentences without a time (VTG, GSA) add to the fix of the second they follow. The date carries over from the last
/// RMC, and moves on a day when the time passes midnight.
/// </para>
/// <para>
/// The satellites come from each talker's latest complete group of GSV sentences (<c>GP</c> for GPS, <c>GL</c> for
/// GLONASS …), and for NMEA 4.1 each signal's, so systems and signals never overwrite each other. A group that is cut
/// short is dropped. A satellite is <see cref="GpsSatellite.Used"/> when the latest GSA for its system lists it.
/// <see cref="Quality"/> keeps each value until a sentence gives it again, since GST and GSV may come less often than
/// once a second.
/// </para>
/// <para>
/// A sentence with a wrong checksum is ignored: on a serial line it was damaged. It is not thread-safe; use it from the
/// loop that reads the GPS.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var gps = new NmeaFixTracker();
/// foreach (var line in lines)
/// {
///     if (NmeaSentence.TryParse(line, out var sentence) &amp;&amp; gps.Update(sentence))
///     {
///         Console.WriteLine($"{gps.Fix.Position} {gps.Fix.Mode} at {gps.Fix.Time}");
///     }
/// }
/// </code>
/// </example>
public sealed class NmeaFixTracker
{
    private readonly Dictionary<(string Talker, int? Signal), List<GpsSatellite>> _inView = [];
    private readonly Dictionary<(string Talker, int? Signal), (int Total, int Next, List<GpsSatellite> List)> _groups =
    [];
    private readonly Dictionary<GnssSystem, HashSet<int>> _used = [];
    private bool _ggaCountsUsed;

    /// <summary>The fix so far: everything the sentences of the latest second have said.</summary>
    public GpsFix Fix { get; private set; } = new();

    /// <summary>How good the fix is, from GGA, GSA, GSV and GST: each value as the latest sentence to give it said.</summary>
    public GpsQuality Quality { get; private set; } = new();

    /// <summary>
    /// The satellites in view, from the latest GSV group of each talker and signal, in the order they were first heard.
    /// The list is replaced, never changed, so a new list means new satellites.
    /// </summary>
    public IReadOnlyList<GpsSatellite> Satellites { get; private set; } = [];

    /// <summary>Adds what a sentence says to <see cref="Fix"/>, <see cref="Quality"/> and <see cref="Satellites"/>.</summary>
    /// <param name="sentence">A sentence from the GPS.</param>
    /// <returns>
    /// Whether <see cref="Fix"/> changed: <see langword="false"/> for a sentence that gives no fix (GSV, GST), has a
    /// wrong checksum, or says nothing new. GSV and GST change only <see cref="Quality"/> and <see cref="Satellites"/>.
    /// </returns>
    public bool Update(NmeaSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        if (!sentence.IsChecksumValid || sentence.Talker == "P")
        {
            return false;
        }

        switch (sentence.Type)
        {
            case "GGA":
                Gga(sentence.Fields);
                break;
            case "GSA":
                Gsa(sentence.Talker, sentence.Fields);
                break;
            case "GSV":
                Gsv(sentence.Talker, sentence.Fields);
                break;
            case "GST":
                Gst(sentence.Fields);
                break;
        }

        if (!sentence.HasFix)
        {
            return false;
        }

        var before = Fix;
        var news = sentence.Fix;
        if (news.UtcTime is { } time && Fix.UtcTime is { } last && time != last)
        {
            // A new second: start again, keeping the date (a day on if the time passed midnight).
            Fix = new GpsFix { UtcDate = time < last ? Fix.UtcDate?.AddDays(1) : Fix.UtcDate };
        }

        Fix = Merge(Fix, news);
        return Fix != before;
    }

    /// <summary>Forgets the fix, its quality and the satellites, for when the GPS is disconnected.</summary>
    public void Reset()
    {
        Fix = new GpsFix();
        Quality = new GpsQuality();
        Satellites = [];
        _inView.Clear();
        _groups.Clear();
        _used.Clear();
        _ggaCountsUsed = false;
    }

    // GGA: time, lat, N/S, lon, E/W, quality, satellites used, HDOP, …
    private void Gga(IReadOnlyList<string> f)
    {
        var used = Int(f, 6);
        _ggaCountsUsed |= used is not null;
        Quality = Quality with { SatellitesUsed = used ?? Quality.SatellitesUsed, Hdop = Number(f, 7) ?? Quality.Hdop };
    }

    // GSA: M/A, mode (1 none, 2 2D, 3 3D), twelve satellite IDs, PDOP, HDOP, VDOP, and from NMEA 4.1 the system ID.
    private void Gsa(string talker, IReadOnlyList<string> f)
    {
        if (Int(f, 1) == 1)
        {
            _used.Clear(); // no fix: none are used
        }
        else
        {
            var declared = Int(f, 17) switch
            {
                1 => GnssSystem.Gps,
                2 => GnssSystem.Glonass,
                3 => GnssSystem.Galileo,
                4 => GnssSystem.BeiDou,
                5 => GnssSystem.Qzss,
                6 => GnssSystem.NavIC,
                _ => TalkerSystem(talker),
            };
            var ids = Enumerable
                .Range(2, 12)
                .Select(i => Int(f, i))
                .OfType<int>()
                .Select(id => (System: SystemOf(declared, id), Id: id))
                .ToList();
            var systems = ids.Select(s => s.System).Append(declared).Where(s => s != GnssSystem.Unknown).Distinct();
            foreach (var system in systems.ToList())
            {
                _used[system] = [.. ids.Where(s => s.System == system).Select(s => s.Id)];
            }
        }

        Quality = Quality with
        {
            Pdop = Number(f, 14) ?? Quality.Pdop,
            Hdop = Number(f, 15) ?? Quality.Hdop,
            Vdop = Number(f, 16) ?? Quality.Vdop,
            SatellitesUsed = _ggaCountsUsed ? Quality.SatellitesUsed : _used.Values.Sum(ids => ids.Count),
        };
        MarkUsed();
    }

    // GSV: sentences in the group, this one's number, satellites in view, then for up to four satellites the ID,
    // elevation, azimuth and SNR; from NMEA 4.1 a signal ID last.
    private void Gsv(string talker, IReadOnlyList<string> f)
    {
        if (Int(f, 0) is not { } total || Int(f, 1) is not { } number || number < 1 || number > total)
        {
            return;
        }

        var signal = (f.Count - 3) % 4 == 1 ? Hex(f[^1]) : null;
        var key = (talker, signal);
        List<GpsSatellite> list;
        if (number == 1)
        {
            list = [];
        }
        else if (_groups.TryGetValue(key, out var group) && group.Total == total && group.Next == number)
        {
            list = group.List;
        }
        else
        {
            _groups.Remove(key); // a sentence was missed: drop the group
            return;
        }

        var system = TalkerSystem(talker);
        for (var i = 3; i + 3 < f.Count; i += 4)
        {
            if (Int(f, i) is { } id)
            {
                list.Add(
                    new GpsSatellite
                    {
                        System = SystemOf(system, id),
                        Id = id,
                        SignalId = signal,
                        ElevationDegrees = Number(f, i + 1),
                        AzimuthDegrees = Number(f, i + 2),
                        SnrDbHz = Number(f, i + 3),
                    }
                );
            }
        }

        if (number < total)
        {
            _groups[key] = (total, number + 1, list);
            return;
        }

        _groups.Remove(key);
        _inView[key] = list;
        MarkUsed();
    }

    // GST: time, RMS of the range residuals, error ellipse (semi-major, semi-minor, orientation), then the standard
    // deviations of latitude, longitude and altitude, in metres.
    private void Gst(IReadOnlyList<string> f)
    {
        var (latitude, longitude) = (Number(f, 5), Number(f, 6));
        Quality = Quality with
        {
            LatitudeErrorMeters = latitude ?? Quality.LatitudeErrorMeters,
            LongitudeErrorMeters = longitude ?? Quality.LongitudeErrorMeters,
            HorizontalErrorMeters =
                latitude is { } lat && longitude is { } lon
                    ? Math.Sqrt((lat * lat) + (lon * lon))
                    : Quality.HorizontalErrorMeters,
            AltitudeErrorMeters = Number(f, 7) ?? Quality.AltitudeErrorMeters,
        };
    }

    /// <summary>Sets <see cref="Satellites"/> from the groups heard, each marked used or not.</summary>
    private void MarkUsed()
    {
        var satellites = _inView
            .Values.SelectMany(group => group)
            .Select(s => s with { Used = _used.TryGetValue(s.System, out var ids) && ids.Contains(s.Id) })
            .ToList();
        if (!satellites.SequenceEqual(Satellites))
        {
            Satellites = satellites;
        }

        var inView = satellites.Select(s => (s.System, s.Id)).Distinct().Count();
        Quality = Quality with { SatellitesInView = _inView.Count > 0 ? inView : Quality.SatellitesInView };
    }

    /// <summary>The system a talker ID stands for; <see cref="GnssSystem.Unknown"/> for <c>GN</c>, several together.</summary>
    private static GnssSystem TalkerSystem(string talker) =>
        talker switch
        {
            "GP" => GnssSystem.Gps,
            "GL" => GnssSystem.Glonass,
            "GA" => GnssSystem.Galileo,
            "GB" or "BD" => GnssSystem.BeiDou,
            "GQ" or "QZ" => GnssSystem.Qzss,
            "GI" => GnssSystem.NavIC,
            _ => GnssSystem.Unknown,
        };

    /// <summary>
    /// A satellite's system from the sentence's and its NMEA ID: GPS sentences carry SBAS (33–64) and QZSS (193–202)
    /// too, and a <c>GN</c> sentence's IDs tell GPS, SBAS and GLONASS (65–96) apart.
    /// </summary>
    private static GnssSystem SystemOf(GnssSystem system, int id) =>
        (system, id) switch
        {
            (GnssSystem.Gps or GnssSystem.Unknown, >= 33 and <= 64) => GnssSystem.Sbas,
            (GnssSystem.Gps or GnssSystem.Unknown, >= 193 and <= 202) => GnssSystem.Qzss,
            (GnssSystem.Unknown, >= 1 and <= 32) => GnssSystem.Gps,
            (GnssSystem.Unknown, >= 65 and <= 96) => GnssSystem.Glonass,
            _ => system,
        };

    private static double? Number(IReadOnlyList<string> f, int index) =>
        index < f.Count
        && double.TryParse(f[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : null;

    private static int? Int(IReadOnlyList<string> f, int index) =>
        index < f.Count && int.TryParse(f[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? Hex(string text) =>
        int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static GpsFix Merge(GpsFix fix, GpsFix news)
    {
        var mode = (fix.Mode, news.Mode) switch
        {
            (_, GpsFixMode.Unknown) => fix.Mode,
            (GpsFixMode.Fix2D or GpsFixMode.Fix3D, GpsFixMode.Fix) => fix.Mode, // keep the more exact one
            _ => news.Mode,
        };
        return new GpsFix
        {
            Mode = mode,
            Position = mode == GpsFixMode.NoFix ? null : news.Position ?? fix.Position,
            UtcTime = news.UtcTime ?? fix.UtcTime,
            UtcDate = news.UtcDate ?? fix.UtcDate,
            SpeedKnots = news.SpeedKnots ?? fix.SpeedKnots,
            Course = news.Course ?? fix.Course,
            AltitudeMeters = news.AltitudeMeters ?? fix.AltitudeMeters,
            Satellites = news.Satellites ?? fix.Satellites,
            Hdop = news.Hdop ?? fix.Hdop,
        };
    }
}
