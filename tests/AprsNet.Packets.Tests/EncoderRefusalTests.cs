namespace AprsNet.Packets.Tests;

/// <summary>
/// Every value the position and Mic-E encoders refuse, with the reason a caller sees. Each refusal is an
/// <see cref="ArgumentException"/> from <see cref="AprsPacket.Create"/>, before anything is sent.
/// </summary>
public class EncoderRefusalTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
    private static readonly AprsSymbol _car = new('/', '>');

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => _address.Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    private static PositionReport Position(
        AprsPosition? position = null,
        bool compressed = false,
        int? course = null,
        double? speed = null,
        PowerHeightGain? phg = null,
        double? range = null,
        double? altitude = null
    ) =>
        new()
        {
            Position = position ?? new AprsPosition(49.05833, -72.02917),
            Symbol = _car,
            IsCompressed = compressed,
            Course = course,
            SpeedKnots = speed,
            PowerHeightGain = phg,
            RangeMiles = range,
            AltitudeFeet = altitude,
        };

    private static MicEReport MicE(
        AprsPosition? position = null,
        AprsSymbol? symbol = null,
        MicEMessage message = MicEMessage.InService,
        int? course = null,
        double? altitude = null,
        string comment = ""
    ) =>
        new()
        {
            Position = position ?? new AprsPosition(33.4273, -112.129),
            Symbol = symbol ?? new AprsSymbol('/', 'j'),
            Message = message,
            Course = course,
            SpeedKnots = course is null ? null : 20,
            AltitudeFeet = altitude,
            Comment = comment,
        };

    // Positions

    [Fact]
    public void RefusesAmbiguityAbove4()
    {
        Rejects(Position(new AprsPosition(49.05833, -72.02917, 5)), "ambiguity must be 0 to 4");
    }

    [Fact]
    public void RefusesPhgPowerAbove81Watts()
    {
        Rejects(Position(phg: new PowerHeightGain(100, 20, 3, null)), "PHG power must be 0 to 81 watts");
    }

    [Fact]
    public void RefusesAnAntennaTooHighForAHeightCode()
    {
        Rejects(Position(phg: new PowerHeightGain(25, 10 << 17, 3, null)), "too large for a height code");
    }

    [Fact]
    public void RefusesAntennaGainAbove9()
    {
        Rejects(Position(phg: new PowerHeightGain(25, 20, 10, null)), "gain must be 0 to 9 dBi");
    }

    [Fact]
    public void RefusesACompressedCourseAbove360()
    {
        Rejects(Position(compressed: true, course: 400, speed: 10), "Course must be 0 to 360");
    }

    [Fact]
    public void RefusesACompressedRangeUnder2Miles()
    {
        Rejects(Position(compressed: true, range: 1), "range is at least 2 miles");
    }

    [Fact]
    public void RefusesACompressedAltitudeTooLargeToEncode()
    {
        Rejects(Position(compressed: true, altitude: 1e12), "too large for a compressed position");
    }

    [Theory]
    [InlineData(1_000_000)]
    [InlineData(-100_000)]
    public void RefusesAnAltitudeTheCommentCannotHold(double feet)
    {
        Rejects(Position(altitude: feet), "Altitude must be -99999 to 999999 feet");
    }

    // Mic-E

    [Fact]
    public void RefusesAMicELatitudeOutOfRange()
    {
        Rejects(MicE(new AprsPosition(91, 10)), "Latitude must be from -90 to 90");
    }

    [Theory]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(-181)]
    public void RefusesAMicELongitudeOf180OrMore(double longitude)
    {
        // Mic-E's longitude degrees stop at 179, so 180 itself cannot be sent either.
        Rejects(MicE(new AprsPosition(10, longitude)), "less than 180 degrees");
    }

    [Fact]
    public void RefusesAMicEReportWithoutASymbol()
    {
        Rejects(MicE(symbol: default(AprsSymbol)), "needs a symbol");
    }

    [Fact]
    public void RefusesAnUnknownMicEMessage()
    {
        Rejects(MicE(message: MicEMessage.Unknown), "standard or custom message, or Emergency");
    }

    [Fact]
    public void RefusesAMicECourseAbove360()
    {
        Rejects(MicE(course: 400), "Course must be 0 to 360");
    }

    [Fact]
    public void RefusesAMicEAltitudeOutOfRange()
    {
        Rejects(MicE(altitude: 3_000_000), "Mic-E altitude must be from");
    }

    [Fact]
    public void RefusesAControlCharacterInAMicEComment()
    {
        Rejects(MicE(comment: "line\nbreak"), "control characters");
    }

    [Fact]
    public void WritesACustomMessageWithAmbiguity()
    {
        // Custom message bits in the ambiguous digits are written as K, not Z.
        var report = MicE(new AprsPosition(33.4273, -112.129, 2), message: MicEMessage.Custom1);

        var decoded = (MicEReport)_address.Create(report).Payload!;

        Assert.Equal(MicEMessage.Custom1, decoded.Message);
        Assert.Equal(2, decoded.Position.Ambiguity);
    }
}
