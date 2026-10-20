using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>
/// One NMEA 0183 sentence, as a GPS sends it (<c>$GPRMC,…*6A</c>) or as a <c>$</c> packet carries it (APRS101 p. 34):
/// its talker, type and fields, whether its checksum is right, and the <see cref="Fix"/> it gives. GGA, RMC, GLL, VTG
/// and GSA give a fix, from any talker (<c>GP</c>, <c>GN</c>, <c>GL</c>, <c>GA</c>, <c>GB</c> …); other sentences are
/// kept with their fields.
/// </summary>
/// <remarks>
/// Parsing never throws. A wrong checksum or a field that can't be read is reported in <see cref="Issues"/>, and the
/// field is left out of the fix. The checksum is optional, as it is in NMEA 0183 version 2 for some sentences.
/// </remarks>
public sealed class NmeaSentence
{
    private static readonly SearchValues<char> _hex = SearchValues.Create("0123456789ABCDEFabcdef");

    private NmeaSentence(string text, string talker, string type, string[] fields)
    {
        Text = text;
        Talker = talker;
        Type = type;
        Fields = fields;
    }

    /// <summary>The sentence as given, without a trailing line ending.</summary>
    public string Text { get; }

    /// <summary>The talker: <c>GP</c> for GPS, <c>GN</c> for several systems together, <c>P</c> for proprietary.</summary>
    public string Talker { get; }

    /// <summary>The sentence type: <c>GGA</c>, <c>RMC</c> …, or for a proprietary sentence the rest of its address.</summary>
    public string Type { get; }

    /// <summary>The fields after the address, before any checksum, exactly as sent; empty ones are empty strings.</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>The checksum sent after <c>*</c>, or <see langword="null"/> when there is none.</summary>
    public byte? Checksum { get; private init; }

    /// <summary>Whether the checksum matches the sentence; <see langword="true"/> when there is no checksum.</summary>
    public bool IsChecksumValid { get; private init; } = true;

    /// <summary>What this sentence says about the fix; <see cref="GpsFixMode.Unknown"/> and no values for other types.</summary>
    public GpsFix Fix { get; private init; } = new();

    /// <summary>
    /// Problems found: <see cref="ParseIssueCode.NmeaChecksumMismatch"/> or <see cref="ParseIssueCode.InvalidNmeaField"/>,
    /// as warnings. Offsets count from the <c>$</c>.
    /// </summary>
    public IReadOnlyList<ParseIssue> Issues { get; private init; } = [];

    /// <summary>Whether the sentence gives a fix: GGA, RMC, GLL, VTG or GSA.</summary>
    public bool HasFix => Type is "GGA" or "RMC" or "GLL" or "VTG" or "GSA" && Talker != "P";

    /// <summary>Reads a sentence. A trailing line ending is ignored.</summary>
    /// <param name="text">The sentence, starting with <c>$</c>.</param>
    /// <param name="sentence">The sentence, when <paramref name="text"/> is one.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="text"/> doesn't start with <c>$</c> and an address of letters and
    /// digits ending in a comma or <c>*</c>. A sentence with a wrong checksum is still read: see <see cref="Issues"/>.
    /// </returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out NmeaSentence? sentence)
    {
        sentence = text is null ? null : Parse(text.TrimEnd('\r', '\n'));
        return sentence is not null;
    }

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <summary>Reads a sentence from bytes, one character per byte (Latin-1), as a packet's information field.</summary>
    internal static NmeaSentence? Parse(ReadOnlySpan<byte> bytes) => Parse(Encoding.Latin1.GetString(bytes));

    private static NmeaSentence? Parse(string text)
    {
        if (text.Length < 4 || text[0] != '$')
        {
            return null;
        }

        var star = text.LastIndexOf('*');
        var body = star >= 0 ? text[1..star] : text[1..];
        var comma = body.IndexOf(',', StringComparison.Ordinal);
        var address = comma >= 0 ? body[..comma] : body;
        if (address.Length < 3 || !address.All(char.IsAsciiLetterOrDigit) || (comma < 0 && star < 0))
        {
            return null;
        }

        var issues = new List<ParseIssue>();
        byte? checksum = null;
        var valid = true;
        if (star >= 0)
        {
            var sent = text.AsSpan(star + 1);
            var expected = body.Aggregate(0, (sum, c) => sum ^ c);
            if (sent.Length == 2 && sent.IndexOfAnyExcept(_hex) < 0)
            {
                checksum = byte.Parse(sent, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                valid = checksum == expected;
            }
            else
            {
                valid = false;
            }

            if (!valid)
            {
                issues.Add(new ParseIssue(ParseIssueCode.NmeaChecksumMismatch, ParseIssueSeverity.Warning, star));
            }
        }

        var (talker, type) = address[0] == 'P' ? ("P", address[1..]) : (address[..2], address[2..]);
        var fields = comma >= 0 ? body[(comma + 1)..].Split(',') : [];
        var reader = new FieldReader(fields, comma + 2, issues);
        var fix = talker == "P" ? new GpsFix() : Decode(type, reader);
        return new NmeaSentence(text, talker, type, fields)
        {
            Checksum = checksum,
            IsChecksumValid = valid,
            Fix = fix,
            Issues = issues,
        };
    }

    private static GpsFix Decode(string type, FieldReader f) =>
        type switch
        {
            // GGA: time, lat, N/S, lon, E/W, quality, satellites, HDOP, altitude, M, …
            "GGA" => Valid(
                new GpsFix
                {
                    UtcTime = f.Time(0),
                    Position = f.Position(1),
                    Mode = f.Int(5) switch
                    {
                        null => GpsFixMode.Unknown,
                        0 => GpsFixMode.NoFix,
                        _ => GpsFixMode.Fix,
                    },
                    Satellites = f.Int(6),
                    Hdop = f.Number(7),
                    AltitudeMeters = f.Number(8),
                }
            ),

            // RMC: time, status, lat, N/S, lon, E/W, speed (knots), course, date, variation, E/W, mode
            "RMC" => Valid(
                new GpsFix
                {
                    UtcTime = f.Time(0),
                    Mode = Status(f.Text(1), f.Text(11)),
                    Position = f.Position(2),
                    SpeedKnots = f.Number(6),
                    Course = f.Number(7),
                    UtcDate = f.Date(8),
                }
            ),

            // GLL: lat, N/S, lon, E/W, time, status, mode (the last three are missing from older GPSs)
            "GLL" => Valid(
                new GpsFix
                {
                    Position = f.Position(0),
                    UtcTime = f.Time(4),
                    Mode = Status(f.Text(5), f.Text(6)),
                }
            ),

            // VTG: course, T, magnetic course, M, knots, N, km/h, K, mode; or, before NMEA 2.0, the four values alone.
            "VTG" => f.Text(1) is "T"
                ? new GpsFix
                {
                    Course = f.Number(0),
                    SpeedKnots = f.Number(4) ?? f.Number(6) / 1.852,
                    Mode = f.Text(8) is "N" ? GpsFixMode.NoFix : GpsFixMode.Unknown,
                }
                : new GpsFix { Course = f.Number(0), SpeedKnots = f.Number(2) },

            // GSA: M/A, mode (1 none, 2 2D, 3 3D), twelve satellite IDs, PDOP, HDOP, VDOP
            "GSA" => new GpsFix
            {
                Mode = f.Int(1) switch
                {
                    1 => GpsFixMode.NoFix,
                    2 => GpsFixMode.Fix2D,
                    3 => GpsFixMode.Fix3D,
                    _ => GpsFixMode.Unknown,
                },
                Satellites = f.Count(2, 12),
                Hdop = f.Number(15),
            },

            _ => new GpsFix(),
        };

    /// <summary>A sentence that says there's no fix keeps no position.</summary>
    private static GpsFix Valid(GpsFix fix) => fix.Mode == GpsFixMode.NoFix ? fix with { Position = null } : fix;

    /// <summary>The fix from RMC's or GLL's status (A valid, V not) and NMEA 2.3's mode (N for none).</summary>
    private static GpsFixMode Status(string status, string mode) =>
        (status, mode) switch
        {
            ("V", _) or (_, "N") => GpsFixMode.NoFix,
            ("A", _) => GpsFixMode.Fix,
            _ => GpsFixMode.Unknown,
        };

    /// <summary>Reads typed values from the fields, reporting those that can't be read.</summary>
    private sealed class FieldReader(string[] fields, int start, List<ParseIssue> issues)
    {
        public string Text(int index) => index < fields.Length ? fields[index] : "";

        public double? Number(int index) =>
            Read(
                index,
                text =>
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && double.IsFinite(v)
                        ? v
                        : (double?)null
            );

        public int? Int(int index) =>
            Read(
                index,
                text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (int?)null
            );

        public int? Count(int index, int length)
        {
            var count = 0;
            for (var i = index; i < index + length && i < fields.Length; i++)
            {
                count += fields[i].Length > 0 ? 1 : 0;
            }

            return index < fields.Length ? count : null;
        }

        /// <summary>hhmmss or hhmmss.sss.</summary>
        public TimeOnly? Time(int index) =>
            Read(
                index,
                text =>
                    text.Length >= 6
                    && int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                    && int.TryParse(text.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                    && double.TryParse(
                        text.AsSpan(4),
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var s
                    )
                    && h < 24
                    && m < 60
                    && s < 60
                        ? new TimeOnly(h, m).Add(TimeSpan.FromMilliseconds(Math.Round(s * 1000)))
                        : (TimeOnly?)null
            );

        /// <summary>ddmmyy, the year taken as 1980–2079 (GPS time starts in 1980).</summary>
        public DateOnly? Date(int index) =>
            Read(
                index,
                text =>
                    text.Length == 6
                    && int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                    && int.TryParse(text.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mo)
                    && int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                    && mo is >= 1 and <= 12
                    && d >= 1
                    && d <= DateTime.DaysInMonth(y < 80 ? 2000 + y : 1900 + y, mo)
                        ? new DateOnly(y < 80 ? 2000 + y : 1900 + y, mo, d)
                        : (DateOnly?)null
            );

        /// <summary>Latitude (ddmm.mmmm) and N/S at <paramref name="index"/>, longitude (dddmm.mmmm) and E/W after.</summary>
        public AprsPosition? Position(int index)
        {
            var latitude = Degrees(index, 90, "N", "S");
            var longitude = Degrees(index + 2, 180, "E", "W");
            return latitude is { } lat && longitude is { } lon ? new AprsPosition(lat, lon) : null;
        }

        private double? Degrees(int index, int limit, string positive, string negative)
        {
            if (Text(index).Length == 0 && Text(index + 1).Length == 0)
            {
                return null;
            }

            var hemisphere = Text(index + 1);
            var value = Read(
                index,
                text =>
                    double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
                    && v % 100 < 60
                    && Math.Floor(v / 100) + (v % 100 / 60) <= limit
                    && (hemisphere == positive || hemisphere == negative)
                        ? Math.Floor(v / 100) + (v % 100 / 60)
                        : (double?)null
            );
            return hemisphere == negative ? -value : value;
        }

        private T? Read<T>(int index, Func<string, T?> read)
            where T : struct
        {
            if (index >= fields.Length || fields[index].Length == 0)
            {
                return default;
            }

            var value = read(fields[index]);
            if (value is null)
            {
                issues.Add(new ParseIssue(ParseIssueCode.InvalidNmeaField, ParseIssueSeverity.Warning, Offset(index)));
            }

            return value;
        }

        private int Offset(int index) => start + fields.Take(index).Sum(field => field.Length + 1);
    }
}
