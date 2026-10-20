namespace AprsNet.Packets;

/// <summary>
/// Puts a GPS's NMEA sentences together into one <see cref="GpsFix"/>. A GPS sends several sentences each second (RMC
/// with the date and speed, GGA with the altitude, GSA with the 2D or 3D fix); hand each one to <see cref="Update"/> and
/// read <see cref="Fix"/>.
/// </summary>
/// <remarks>
/// <para>
/// A sentence with a new time of day starts a new fix, so values from an older second never mix with a newer one.
/// Sentences without a time (VTG, GSA) add to the fix of the second they follow. The date carries over from the last
/// RMC, and moves on a day when the time passes midnight.
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
    /// <summary>The fix so far: everything the sentences of the latest second have said.</summary>
    public GpsFix Fix { get; private set; } = new();

    /// <summary>Adds what a sentence says to <see cref="Fix"/>.</summary>
    /// <param name="sentence">A sentence from the GPS.</param>
    /// <returns>
    /// Whether <see cref="Fix"/> changed: <see langword="false"/> for a sentence that gives no fix, has a wrong checksum,
    /// or says nothing new.
    /// </returns>
    public bool Update(NmeaSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        if (!sentence.HasFix || !sentence.IsChecksumValid)
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

    /// <summary>Forgets the fix, for when the GPS is disconnected.</summary>
    public void Reset() => Fix = new GpsFix();

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
