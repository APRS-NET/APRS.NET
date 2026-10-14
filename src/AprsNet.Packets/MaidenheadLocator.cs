using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AprsNet.Packets;

/// <summary>
/// A Maidenhead (IARU) locator of 4, 6, 8 or 10 characters, such as <c>FN31pr</c>, and the squaroid of
/// latitude and longitude it names: its centre and its south-west and north-east corners.
/// </summary>
/// <remarks>
/// <para>
/// Follows the IARU Region 1 VHF Handbook, version 10.03 (February 2026), chapter 20 "The locator system", section 20.2
/// "Definition of the IARU locator" (<see href="https://www.iaru-r1.org/"/>). Pairs of characters divide longitude
/// and latitude in turn: a field (letters <c>A</c>–<c>R</c>, 20° × 10°), a square (digits, 2° × 1°), a subsquare
/// (letters <c>A</c>–<c>X</c>, 5′ × 2.5′), an extended square (digits, 0.5′ × 0.25′) and an extended subsquare
/// (letters <c>A</c>–<c>X</c>), each counted west to east and south to north from 180° W and 90° S.
/// </para>
/// <para>
/// As the handbook's test cases require, longitude 180° is the same meridian as 180° W and falls in field
/// <c>A</c>, latitude 90° falls in the last row, and at the poles, where longitude is undefined, the longitude
/// characters are the lowest (<c>AA00AA</c>, <c>AR09AX</c>). The handbook writes locators in upper case and
/// recommends accepting any case; parsing ignores case, and <see cref="Value"/> uses the common written form with
/// lower-case subsquare letters (<c>FN31pr</c>).
/// </para>
/// </remarks>
public sealed record MaidenheadLocator
{
    // Divisions per character pair, for longitude and latitude alike: field, square, subsquare, extended square,
    // extended subsquare.
    private static readonly int[] _divisions = [18, 10, 24, 10, 24];

    private MaidenheadLocator(string value, AprsPosition southWest, AprsPosition northEast)
    {
        Value = value;
        SouthWest = southWest;
        NorthEast = northEast;
        Center = new AprsPosition(
            (southWest.Latitude + northEast.Latitude) / 2,
            (southWest.Longitude + northEast.Longitude) / 2
        );
    }

    /// <summary>
    /// The locator in its written form: field letters in upper case, subsquare letters in lower case, for example
    /// <c>FN31pr</c> or <c>JN18du55ix</c>.
    /// </summary>
    public string Value { get; }

    /// <summary>The number of characters, 4, 6, 8 or 10: more characters name a smaller squaroid.</summary>
    public int Length => Value.Length;

    /// <summary>The centre of the squaroid, which the IARU uses as the locator's reference location.</summary>
    public AprsPosition Center { get; }

    /// <summary>The south-west corner of the squaroid: its lowest latitude and longitude.</summary>
    public AprsPosition SouthWest { get; }

    /// <summary>The north-east corner of the squaroid: its highest latitude and longitude.</summary>
    public AprsPosition NorthEast { get; }

    /// <summary>
    /// Reads a locator of 4, 6, 8 or 10 characters, ignoring case and surrounding whitespace. For user input, use
    /// <see cref="TryParse(string?, out MaidenheadLocator?, out MaidenheadParseError?)"/>, which says what is wrong.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="text"/> is not a locator; the message says why.</exception>
    public static MaidenheadLocator Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var locator, out var error) ? locator : throw new FormatException(error.Message);
    }

    /// <summary>Reads a locator as <see cref="Parse"/> does, or returns <see langword="false"/>. Never throws.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out MaidenheadLocator? locator) =>
        TryParse(text, out locator, out _);

    /// <summary>
    /// Reads a locator as <see cref="Parse"/> does, or returns <see langword="false"/> with the reason in
    /// <paramref name="error"/>: empty, a character out of range for its position, or the wrong length. Characters
    /// are checked before the length, so a typo is reported at the character. Never throws.
    /// </summary>
    public static bool TryParse(
        [NotNullWhen(true)] string? text,
        [NotNullWhen(true)] out MaidenheadLocator? locator,
        [NotNullWhen(false)] out MaidenheadParseError? error
    )
    {
        locator = null;
        var trimmed = (text ?? "").AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            error = new MaidenheadParseError(MaidenheadParseErrorKind.Empty);
            return false;
        }

        var start = text!.AsSpan().IndexOf(trimmed); // so the error points into the text as given
        Span<int> longitude = stackalloc int[5];
        Span<int> latitude = stackalloc int[5];
        for (var i = 0; i < Math.Min(trimmed.Length, 10); i++)
        {
            var pair = i / 2;
            if (Digit(trimmed[i], pair) is not { } digit)
            {
                error = new MaidenheadParseError(start + i, i + 1, trimmed[i]);
                return false;
            }

            (i % 2 == 0 ? longitude : latitude)[pair] = digit;
        }

        if (trimmed.Length is not (4 or 6 or 8 or 10))
        {
            error = new MaidenheadParseError(MaidenheadParseErrorKind.WrongLength);
            return false;
        }

        var pairs = trimmed.Length / 2;
        var (lonIndex, latIndex, cells) = (0L, 0L, 1L);
        for (var pair = 0; pair < pairs; pair++)
        {
            cells *= _divisions[pair];
            lonIndex = (lonIndex * _divisions[pair]) + longitude[pair];
            latIndex = (latIndex * _divisions[pair]) + latitude[pair];
        }

        locator = Create(lonIndex, latIndex, pairs, cells);
        error = null;
        return true;
    }

    /// <summary>
    /// The locator of the squaroid that contains a position, with <paramref name="length"/> characters (4, 6, 8 or
    /// 10). Longitude 180° falls in field <c>A</c> with 180° W, latitude 90° in the last row, and at the poles the
    /// longitude characters are the lowest (see the remarks).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The latitude is not from -90 to 90, the longitude not from -180 to 180, or the length not 4, 6, 8 or 10.
    /// </exception>
    public static MaidenheadLocator FromPosition(double latitude, double longitude, int length)
    {
        if (!(latitude is >= -90 and <= 90))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be from -90 to 90.");
        }

        if (!(longitude is >= -180 and <= 180))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be from -180 to 180.");
        }

        if (length is not (4 or 6 or 8 or 10))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A locator has 4, 6, 8 or 10 characters.");
        }

        // Work in the finest cells (10 characters): 2880 per degree of longitude, 5760 per degree of latitude. A
        // value within floating-point error of a cell edge counts as on the edge, so 2° W lands in IO90, not IO89.
        const long fineCells = 18L * 10 * 24 * 10 * 24;
        var atPole = latitude is 90 or -90;
        var lonFine = atPole ? 0 : (long)Math.Floor(((longitude + 180) * (fineCells / 360.0)) + 1e-6) % fineCells;
        var latFine = Math.Min((long)Math.Floor(((latitude + 90) * (fineCells / 180.0)) + 1e-6), fineCells - 1);

        var pairs = length / 2;
        var cells = 1L;
        for (var pair = 0; pair < pairs; pair++)
        {
            cells *= _divisions[pair];
        }

        var scale = fineCells / cells;
        return Create(lonFine / scale, latFine / scale, pairs, cells);
    }

    /// <summary>The locator of the squaroid that contains <paramref name="position"/>; see <see cref="FromPosition(double, double, int)"/>.</summary>
    public static MaidenheadLocator FromPosition(AprsPosition position, int length) =>
        FromPosition(position.Latitude, position.Longitude, length);

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;

    /// <summary>Whether <paramref name="text"/> is a locator of exactly <paramref name="length"/> characters, in any case.</summary>
    internal static bool IsValid(ReadOnlySpan<byte> text, int length)
    {
        if (text.Length != length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (Digit((char)text[i], i / 2) is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The value of character <paramref name="c"/> in pair <paramref name="pair"/>, or null if out of range.</summary>
    private static int? Digit(char c, int pair)
    {
        var letters = pair % 2 == 0; // field and subsquares are letters, squares are digits
        var value = letters ? (c | 0x20) - 'a' : c - '0';
        var limit = letters ? (pair == 0 ? 18 : 24) : 10;
        return (letters ? char.IsAsciiLetter(c) : char.IsAsciiDigit(c)) && value < limit ? value : null;
    }

    private static MaidenheadLocator Create(long lonIndex, long latIndex, int pairs, long cells)
    {
        var width = 360.0 / cells;
        var height = 180.0 / cells;
        var southWest = new AprsPosition(-90 + (latIndex * height), -180 + (lonIndex * width));
        var northEast = new AprsPosition(-90 + ((latIndex + 1) * height), -180 + ((lonIndex + 1) * width));

        Span<char> value = stackalloc char[pairs * 2];
        var (lon, lat) = (lonIndex, latIndex);
        for (var pair = pairs - 1; pair >= 0; pair--)
        {
            var divisions = _divisions[pair];
            var first =
                pair == 0 ? 'A'
                : pair % 2 == 0 ? 'a'
                : '0';
            value[pair * 2] = (char)(first + (lon % divisions));
            value[(pair * 2) + 1] = (char)(first + (lat % divisions));
            lon /= divisions;
            lat /= divisions;
        }

        return new MaidenheadLocator(value.ToString(), southWest, northEast);
    }
}

/// <summary>Which pair of characters in a Maidenhead locator: each pair is a longitude character, then a latitude one.</summary>
public enum MaidenheadLocatorPart
{
    /// <summary>Characters 1 and 2: letters <c>A</c>–<c>R</c>, 20° of longitude by 10° of latitude.</summary>
    Field,

    /// <summary>Characters 3 and 4: digits <c>0</c>–<c>9</c>, 2° by 1°.</summary>
    Square,

    /// <summary>Characters 5 and 6: letters <c>A</c>–<c>X</c>, 5′ by 2.5′.</summary>
    Subsquare,

    /// <summary>Characters 7 and 8: digits <c>0</c>–<c>9</c>, 0.5′ by 0.25′.</summary>
    ExtendedSquare,

    /// <summary>Characters 9 and 10: letters <c>A</c>–<c>X</c>.</summary>
    ExtendedSubsquare,
}

/// <summary>Why text is not a Maidenhead locator.</summary>
public enum MaidenheadParseErrorKind
{
    /// <summary>The text is empty or only whitespace.</summary>
    Empty,

    /// <summary>The characters are valid, but there are not 4, 6, 8 or 10 of them.</summary>
    WrongLength,

    /// <summary>A character is out of range for its position, for example a letter where a digit belongs.</summary>
    InvalidCharacter,
}

/// <summary>
/// Why <see cref="MaidenheadLocator.TryParse(string?, out MaidenheadLocator?, out MaidenheadParseError?)"/> rejected
/// some text, with an English <see cref="Message"/> a UI can show.
/// </summary>
public sealed class MaidenheadParseError
{
    internal MaidenheadParseError(MaidenheadParseErrorKind kind)
    {
        Kind = kind;
        Index = -1;
        Message =
            kind == MaidenheadParseErrorKind.Empty
                ? "Enter a locator, for example FN31pr."
                : "A locator has 4, 6, 8 or 10 characters.";
    }

    internal MaidenheadParseError(int index, int number, char character)
    {
        Kind = MaidenheadParseErrorKind.InvalidCharacter;
        Index = index;
        Character = character;
        Part = (MaidenheadLocatorPart)((number - 1) / 2);
        var expected = Part switch
        {
            MaidenheadLocatorPart.Field => "a letter A-R (field)",
            MaidenheadLocatorPart.Square => "a digit 0-9 (square)",
            MaidenheadLocatorPart.Subsquare => "a letter A-X (subsquare)",
            MaidenheadLocatorPart.ExtendedSquare => "a digit 0-9 (extended square)",
            _ => "a letter A-X (extended subsquare)",
        };
        Message = string.Create(
            CultureInfo.InvariantCulture,
            $"Character {number} ('{character}') must be {expected}."
        );
    }

    /// <summary>What is wrong.</summary>
    public MaidenheadParseErrorKind Kind { get; }

    /// <summary>
    /// For <see cref="MaidenheadParseErrorKind.InvalidCharacter"/>, the zero-based index of the character in the text
    /// as given, counting any leading whitespace, so a UI can highlight it; otherwise -1.
    /// </summary>
    public int Index { get; }

    /// <summary>For <see cref="MaidenheadParseErrorKind.InvalidCharacter"/>, the character that is out of range.</summary>
    public char? Character { get; }

    /// <summary>For <see cref="MaidenheadParseErrorKind.InvalidCharacter"/>, the pair the character belongs to.</summary>
    public MaidenheadLocatorPart? Part { get; }

    /// <summary>
    /// The reason in English, for example <c>Character 3 ('Z') must be a digit 0-9 (square).</c> Characters are
    /// counted from 1, after any leading whitespace.
    /// </summary>
    public string Message { get; }

    /// <summary>Returns <see cref="Message"/>.</summary>
    public override string ToString() => Message;
}
