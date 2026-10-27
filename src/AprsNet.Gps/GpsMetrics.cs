using System.Diagnostics.Metrics;
using AprsNet.Packets;

namespace AprsNet.Gps;

/// <summary>What confidence a GPS's error estimates are given at: see <see cref="GpsQuality"/>.</summary>
public enum GpsErrorConfidence
{
    /// <summary>95%, as gpsd gives them.</summary>
    Percent95,

    /// <summary>One standard deviation, about 68%, as NMEA's GST gives them.</summary>
    OneSigma,
}

/// <summary>
/// Records one GPS's quality over time on the <c>AprsNet.Gps</c> meter, so a dashboard can show its fixes coming and
/// going: the time spent in each fix state, fixes lost, the satellites used and in view by system, the dilutions of
/// precision, the estimated errors and the average signal. Every measurement carries a <c>gps</c> tag naming the GPS,
/// and never a position.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="GpsdConnection"/> records its own, tagged with its <see cref="GpsdConnection.Server"/>. For a GPS read
/// as NMEA, hand each update of an <see cref="NmeaFixTracker"/> to <see cref="Record(NmeaFixTracker)"/>, and call
/// <see cref="Reset"/> when the GPS is disconnected so the time it was away isn't counted.
/// </para>
/// <para>
/// The time between two records counts toward the fix state of the first, up to <see cref="MaxInterval"/>: a longer
/// wait means the GPS went quiet, and that time is left out. Nothing is recorded unless something listens to the meter.
/// It is thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var gps = new NmeaFixTracker();
/// var metrics = new GpsMetrics("/dev/ttyACM0", GpsErrorConfidence.OneSigma);
/// if (NmeaSentence.TryParse(line, out var sentence) &amp;&amp; gps.Update(sentence))
/// {
///     metrics.Record(gps);
/// }
/// </code>
/// </example>
public sealed class GpsMetrics
{
    /// <summary>The meter's name: <c>AprsNet.Gps</c>.</summary>
    public const string MeterName = "AprsNet.Gps";

    private static readonly Meter _meter = new(MeterName);

    private static readonly Counter<double> _fixTime = _meter.CreateCounter<double>(
        "aprsnet.gps.fix.time",
        "s",
        "Time spent in each fix state, by mode: 3d, 2d, fix (a fix of unknown dimension) or none."
    );

    private static readonly Counter<long> _fixLosses = _meter.CreateCounter<long>(
        "aprsnet.gps.fix.losses",
        "{loss}",
        "Times the GPS went from a fix to no fix."
    );

    private static readonly Gauge<int> _used = _meter.CreateGauge<int>(
        "aprsnet.gps.satellites.used",
        "{satellite}",
        "Satellites the fix uses, by system."
    );

    private static readonly Gauge<int> _inView = _meter.CreateGauge<int>(
        "aprsnet.gps.satellites.in_view",
        "{satellite}",
        "Satellites the GPS can see, used or not, by system."
    );

    private static readonly Gauge<double> _dop = _meter.CreateGauge<double>(
        "aprsnet.gps.dop",
        "1",
        "Dilutions of precision, by kind: pdop, hdop, vdop, tdop or gdop. Lower is better."
    );

    private static readonly Gauge<double> _error = _meter.CreateGauge<double>(
        "aprsnet.gps.error",
        "m",
        "The GPS's estimated error, by axis (horizontal, vertical, latitude or longitude) and confidence: "
            + "95_percent from gpsd, one_sigma from NMEA's GST."
    );

    private static readonly Gauge<double> _signal = _meter.CreateGauge<double>(
        "aprsnet.gps.signal",
        "dB-Hz",
        "The average signal-to-noise ratio of the satellites the fix uses."
    );

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;
    private readonly KeyValuePair<string, object?> _gpsTag;
    private readonly KeyValuePair<string, object?> _confidenceTag;
    private readonly HashSet<GnssSystem> _systems = [];
    private GpsFixMode _mode = GpsFixMode.Unknown;
    private long _since;

    /// <summary>Creates a recorder for the GPS named <paramref name="gps"/>, on the system clock.</summary>
    /// <param name="gps">The <c>gps</c> tag: a name for this GPS, such as its port or gpsd's address.</param>
    /// <param name="errorConfidence">What confidence the GPS's error estimates are given at.</param>
    /// <exception cref="ArgumentException"><paramref name="gps"/> is empty.</exception>
    public GpsMetrics(string gps, GpsErrorConfidence errorConfidence)
        : this(gps, errorConfidence, TimeProvider.System) { }

    /// <summary>Creates a recorder for the GPS named <paramref name="gps"/>.</summary>
    /// <param name="gps">The <c>gps</c> tag: a name for this GPS, such as its port or gpsd's address.</param>
    /// <param name="errorConfidence">What confidence the GPS's error estimates are given at.</param>
    /// <param name="timeProvider">The clock that times the fix states.</param>
    /// <exception cref="ArgumentException"><paramref name="gps"/> is empty.</exception>
    public GpsMetrics(string gps, GpsErrorConfidence errorConfidence, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gps);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Gps = gps;
        ErrorConfidence = errorConfidence;
        _timeProvider = timeProvider;
        _gpsTag = new("gps", gps);
        _confidenceTag = new(
            "confidence",
            errorConfidence == GpsErrorConfidence.Percent95 ? "95_percent" : "one_sigma"
        );
    }

    /// <summary>
    /// The longest time between two records that counts toward a fix state: 10 seconds. A GPS reports every second.
    /// </summary>
    public static TimeSpan MaxInterval { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The <c>gps</c> tag.</summary>
    public string Gps { get; }

    /// <summary>What confidence the error estimates are given at, as the <c>confidence</c> tag says.</summary>
    public GpsErrorConfidence ErrorConfidence { get; }

    /// <summary>Records what an <see cref="NmeaFixTracker"/> says now.</summary>
    public void Record(NmeaFixTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        Record(tracker.Fix, tracker.Quality, tracker.Satellites);
    }

    /// <summary>Records a fix, its quality and the satellites in view.</summary>
    /// <param name="fix">The fix: its mode times the fix states and counts losses. Its position isn't recorded.</param>
    /// <param name="quality">The DOPs and error estimates; values it doesn't give aren't recorded.</param>
    /// <param name="satellites">The satellites in view, counted by system; an empty list records no counts.</param>
    public void Record(GpsFix fix, GpsQuality quality, IReadOnlyList<GpsSatellite> satellites)
    {
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(satellites);
        lock (_lock)
        {
            RecordMode(fix.Mode);
            RecordSatellites(satellites);
        }

        Dop("pdop", quality.Pdop);
        Dop("hdop", quality.Hdop);
        Dop("vdop", quality.Vdop);
        Dop("tdop", quality.Tdop);
        Dop("gdop", quality.Gdop);
        Error("horizontal", quality.HorizontalErrorMeters);
        Error("vertical", quality.AltitudeErrorMeters);
        Error("latitude", quality.LatitudeErrorMeters);
        Error("longitude", quality.LongitudeErrorMeters);
        var signals = satellites.Where(s => s.Used && s.SnrDbHz > 0).Select(s => s.SnrDbHz!.Value).ToList();
        if (signals.Count > 0)
        {
            _signal.Record(signals.Average(), _gpsTag);
        }
    }

    /// <summary>
    /// Forgets the fix state, for when the GPS is disconnected: the time until the next record isn't counted, and
    /// losing the fix that way isn't a fix lost.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _mode = GpsFixMode.Unknown;
            _systems.Clear();
        }
    }

    private void RecordMode(GpsFixMode mode)
    {
        var now = _timeProvider.GetTimestamp();
        if (_mode != GpsFixMode.Unknown)
        {
            var elapsed = _timeProvider.GetElapsedTime(_since, now);
            _fixTime.Add(
                Math.Min(elapsed.TotalSeconds, MaxInterval.TotalSeconds),
                _gpsTag,
                new("mode", ModeName(_mode))
            );
        }

        if (mode == GpsFixMode.Unknown)
        {
            return; // the source didn't say: carry on timing the last state
        }

        if (mode == GpsFixMode.NoFix && _mode is GpsFixMode.Fix or GpsFixMode.Fix2D or GpsFixMode.Fix3D)
        {
            _fixLosses.Add(1, _gpsTag);
        }

        _mode = mode;
        _since = now;
    }

    private void RecordSatellites(IReadOnlyList<GpsSatellite> satellites)
    {
        if (satellites.Count == 0)
        {
            return;
        }

        // A satellite heard on several signals counts once.
        var bySystem = satellites
            .GroupBy(s => s.System)
            .ToDictionary(
                g => g.Key,
                g =>
                    (
                        InView: g.Select(s => s.Id).Distinct().Count(),
                        Used: g.Where(s => s.Used).Select(s => s.Id).Distinct().Count()
                    )
            );
        // A system no longer in view goes to zero rather than keeping its last count.
        foreach (var system in _systems.Union(bySystem.Keys).ToList())
        {
            var (inView, used) = bySystem.GetValueOrDefault(system);
            var tag = new KeyValuePair<string, object?>("system", SystemName(system));
            _inView.Record(inView, _gpsTag, tag);
            _used.Record(used, _gpsTag, tag);
            _systems.Add(system);
        }
    }

    private void Dop(string kind, double? value)
    {
        if (value is { } dop)
        {
            _dop.Record(dop, _gpsTag, new("kind", kind));
        }
    }

    private void Error(string axis, double? meters)
    {
        if (meters is { } value)
        {
            _error.Record(value, _gpsTag, new("axis", axis), _confidenceTag);
        }
    }

    private static string ModeName(GpsFixMode mode) =>
        mode switch
        {
            GpsFixMode.Fix3D => "3d",
            GpsFixMode.Fix2D => "2d",
            GpsFixMode.Fix => "fix",
            _ => "none",
        };

    private static string SystemName(GnssSystem system) =>
        system switch
        {
            GnssSystem.Gps => "gps",
            GnssSystem.Sbas => "sbas",
            GnssSystem.Glonass => "glonass",
            GnssSystem.Galileo => "galileo",
            GnssSystem.BeiDou => "beidou",
            GnssSystem.Qzss => "qzss",
            GnssSystem.NavIC => "navic",
            _ => "unknown",
        };
}
