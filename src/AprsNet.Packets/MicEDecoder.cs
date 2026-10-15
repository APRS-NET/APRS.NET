namespace AprsNet.Packets;

/// <summary>
/// Decodes Mic-E reports (APRS101 ch. 10, with the APRS 1.1 errata and the APRS 1.2 type codes, device codes and
/// speed rule). The latitude, message and longitude flags are in the destination; the rest is in the information field.
/// </summary>
internal static class MicEDecoder
{
    // Trailing manufacturer and version bytes after a ` or ' type byte (APRS 1.2 device table).
    private static readonly Dictionary<string, string> _devices = new(StringComparer.Ordinal)
    {
        ["_ "] = "VX-8",
        ["_\""] = "FTM-350",
        ["_#"] = "VX-8G",
        ["_$"] = "FT1D",
        ["_%"] = "FTM-400DR",
        ["_)"] = "FTM-100D",
        ["_("] = "FT2D",
        ["_0"] = "FT3D",
        ["_3"] = "FT5D",
        ["_1"] = "FTM-300D",
        [" X"] = "AP510",
        ["(5"] = "D578UV",
        ["(8"] = "D878UV",
        ["|3"] = "TinyTrack3",
        ["|4"] = "TinyTrack4",
        [":4"] = "DR-7400",
        [":8"] = "DR-7800",
    };

    /// <summary>
    /// Decodes a Mic-E report. <paramref name="offset"/> is where the information field starts in Raw and
    /// <paramref name="destinationOffset"/> where the destination does.
    /// </summary>
    public static MicEReport? Decode(
        ReadOnlyMemory<byte> information,
        string destination,
        int offset,
        int destinationOffset,
        List<ParseIssue> issues
    )
    {
        var info = information.Span;
        if (info.Length < 9)
        {
            issues.Add(Error(ParseIssueCode.PositionTooShort, offset));
            return null;
        }

        var dash = destination.IndexOf('-', StringComparison.Ordinal);
        if (!TryDestination(dash < 0 ? destination : destination[..dash], out var fields))
        {
            issues.Add(Error(ParseIssueCode.InvalidMicEDestination, destinationOffset));
            return null;
        }

        if (!TryLongitude(info[1..4], fields, out var longitude))
        {
            issues.Add(Error(ParseIssueCode.InvalidLongitude, offset + 1));
            return null;
        }

        if (!PositionDecoder.TrySymbol(info[8], info[7], offset + 7, issues, out var symbol))
        {
            return null;
        }

        var (speed, course) = SpeedCourse(info[4], info[5], info[6]);

        // The text: an optional type byte, an optional xxx} altitude, the status, an optional trailing device code.
        var text = information[9..];
        var type =
            text.Length > 0 && text.Span[0] is (byte)' ' or (byte)'>' or (byte)']' or (byte)'`' or (byte)'\''
                ? (char)text.Span[0]
                : (char?)null;
        var status = type is null ? text : text[1..];
        double? altitudeFeet = null;
        if (status.Length >= 4 && status.Span[3] == '}' && TryBase91(status.Span[..3], out var meters))
        {
            altitudeFeet = (meters - 10000) / 0.3048;
            status = status[4..];
        }

        var device = Device(type, ref status);
        var (position, datum, comment) = PositionDecoder.ApplyDao(
            new AprsPosition(fields.Latitude, longitude, fields.Ambiguity),
            CommentTelemetry.Strip(status.Span, out var telemetry)
        );
        var locator = StatusDecoder.TryLocator(status.Span, out _)?.Locator;

        return new MicEReport
        {
            Position = position,
            Symbol = symbol,
            Course = course,
            SpeedKnots = speed,
            AltitudeFeet = altitudeFeet ?? PositionDecoder.CommentAltitude(status.Span),
            Datum = datum,
            Telemetry = telemetry,
            ExtensionBytes = information[..7],
            CommentBytes = text,
            Comment = comment,
            Message = fields.Message,
            IsCurrentFix = info[0] is (byte)'`' or 0x1C || (info[0] == '\'' && type == ']'),
            IsMessagingCapable = type is '>' or ']' or '`',
            Device = device,
            MaidenheadLocator = locator,
        };
    }

    /// <summary>The fields carried by the six destination characters.</summary>
    private readonly record struct DestinationFields(
        double Latitude,
        int Ambiguity,
        MicEMessage Message,
        bool LongitudeOffset,
        bool West
    );

    /// <summary>
    /// Reads the destination: per character a latitude digit (or a space for ambiguity) and a flag. Characters 1–3
    /// give the message bits (standard <c>P</c>–<c>Z</c>, custom <c>A</c>–<c>K</c>), 4 north, 5 the +100° longitude
    /// offset and 6 west, each set by <c>P</c>–<c>Z</c>.
    /// </summary>
    private static bool TryDestination(string destination, out DestinationFields fields)
    {
        fields = default;
        if (destination.Length != 6)
        {
            return false;
        }

        Span<int> digits = stackalloc int[6];
        Span<char> kinds = stackalloc char[6]; // '0' plain, 'C' custom, 'S' standard
        for (var i = 0; i < 6; i++)
        {
            var c = destination[i];
            (digits[i], kinds[i]) = c switch
            {
                >= '0' and <= '9' => (c - '0', '0'),
                'L' => (-1, '0'),
                >= 'A' and <= 'J' when i < 3 => (c - 'A', 'C'),
                'K' when i < 3 => (-1, 'C'),
                >= 'P' and <= 'Y' => (c - 'P', 'S'),
                'Z' => (-1, 'S'),
                _ => (-2, '?'),
            };
            if (digits[i] == -2)
            {
                return false;
            }
        }

        // Spaces may only blank trailing digits: hundredths, then minutes.
        var ambiguity = 0;
        foreach (var index in (ReadOnlySpan<int>)[5, 4, 3, 2])
        {
            if (digits[index] != -1)
            {
                break;
            }

            ambiguity++;
        }

        for (var i = 0; i < 6; i++)
        {
            if (digits[i] == -1 && i < 6 - ambiguity)
            {
                return false;
            }

            digits[i] = Math.Max(digits[i], 0);
        }

        var degrees = (digits[0] * 10) + digits[1];
        var minutes = (digits[2] * 10) + digits[3] + (((digits[4] * 10) + digits[5]) / 100.0);
        var latitude = degrees + (minutes / 60);
        if (minutes >= 60 || latitude > 90)
        {
            return false;
        }

        fields = new DestinationFields(
            kinds[3] == 'S' ? latitude : -latitude,
            ambiguity,
            MessageFromBits(kinds[..3]),
            kinds[4] == 'S',
            kinds[5] == 'S'
        );
        return true;
    }

    /// <summary>The message from the first three destination characters' bits (APRS101 ch. 10 table).</summary>
    private static MicEMessage MessageFromBits(ReadOnlySpan<char> kinds)
    {
        var custom = kinds.Contains('C');
        if (custom && kinds.Contains('S'))
        {
            return MicEMessage.Unknown;
        }

        var bits = (kinds[0] != '0' ? 4 : 0) + (kinds[1] != '0' ? 2 : 0) + (kinds[2] != '0' ? 1 : 0);
        return bits == 0 ? MicEMessage.Emergency
            : custom ? MicEMessage.Custom0 + (7 - bits)
            : MicEMessage.OffDuty + (7 - bits);
    }

    /// <summary>Longitude from degrees, minutes and hundredths bytes, each offset by 28.</summary>
    private static bool TryLongitude(ReadOnlySpan<byte> bytes, DestinationFields fields, out double longitude)
    {
        longitude = 0;
        var degrees = bytes[0] - 28 + (fields.LongitudeOffset ? 100 : 0);
        if (degrees is >= 180 and <= 189)
        {
            degrees -= 80;
        }
        else if (degrees is >= 190 and <= 199)
        {
            degrees -= 190;
        }

        var minutes = bytes[1] - 28;
        if (minutes >= 60)
        {
            minutes -= 60;
        }

        var hundredths = bytes[2] - 28;
        if (degrees is < 0 or > 179 || minutes is < 0 or > 59 || hundredths is < 0 or > 99)
        {
            return false;
        }

        // The latitude's ambiguity blanks the same digits of the longitude.
        hundredths = fields.Ambiguity switch
        {
            0 => hundredths,
            1 => hundredths / 10 * 10,
            _ => 0,
        };
        minutes = fields.Ambiguity switch
        {
            3 => minutes / 10 * 10,
            4 => 0,
            _ => minutes,
        };
        longitude = degrees + ((minutes + (hundredths / 100.0)) / 60);
        longitude = fields.West ? -longitude : longitude;
        return true;
    }

    /// <summary>
    /// Speed and course from three bytes offset by 28 (APRS101 ch. 10, with the APRS 1.1 table corrections). Speeds over
    /// 670 knots use the APRS 1.2 rule S × 112 − 74370, for objects such as the ISS.
    /// </summary>
    private static (double? Speed, int? Course) SpeedCourse(byte sp, byte dc, byte se)
    {
        if (sp < 28 || dc < 28 || se < 28)
        {
            return (null, null);
        }

        var speed = ((sp - 28) * 10) + ((dc - 28) / 10);
        var course = ((dc - 28) % 10 * 100) + (se - 28);
        speed = speed >= 800 ? speed - 800 : speed;
        course = course >= 400 ? course - 400 : course;
        return (speed > 670 ? (speed * 112) - 74370 : speed, course <= 360 ? course : null);
    }

    /// <summary>
    /// The device from the type byte and the trailing manufacturer code, removing a recognised code from the status
    /// (APRS 1.2, A12-MICE-02).
    /// </summary>
    private static string? Device(char? type, ref ReadOnlyMemory<byte> status)
    {
        switch (type)
        {
            case '>':
                return Suffix(ref status, '=', "TH-D72") ?? Suffix(ref status, '^', "TH-D74") ?? "TH-D7";
            case ']':
                return Suffix(ref status, '=', "TM-D710") ?? "TM-D700";
            case '`' or '\'' when status.Length >= 2:
                var code = System.Text.Encoding.Latin1.GetString(status.Span[^2..]);
                if (_devices.TryGetValue(code, out var device))
                {
                    status = status[..^2];
                    return device;
                }

                return null;
            default:
                return null;
        }

        static string? Suffix(ref ReadOnlyMemory<byte> status, char code, string name)
        {
            if (status.Length == 0 || status.Span[^1] != code)
            {
                return null;
            }

            status = status[..^1];
            return name;
        }
    }

    private static bool TryBase91(ReadOnlySpan<byte> bytes, out int value)
    {
        value = 0;
        foreach (var b in bytes)
        {
            if (b is < 33 or > 123)
            {
                return false;
            }

            value = (value * 91) + (b - 33);
        }

        return true;
    }

    private static ParseIssue Error(ParseIssueCode code, int offset) => new(code, ParseIssueSeverity.Error, offset);
}
