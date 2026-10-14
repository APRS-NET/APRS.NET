using System.Text;

namespace AprsNet.Packets.Tests;

public class ObjectItemCreateTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
    private static readonly AprsPosition _position = new(49 + (3.5 / 60), -(72 + (1.75 / 60)));

    private static string Information(AprsPayload payload) =>
        Encoding.Latin1.GetString(_address.Create(payload).Information.Span);

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => _address.Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    private static ObjectReport Leader(string name = "LEADER", bool isLive = true) =>
        new()
        {
            Name = name,
            IsLive = isLive,
            Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
            Position = _position,
            Symbol = new AprsSymbol('/', '>'),
            Course = 88,
            SpeedKnots = 36,
        };

    private static ItemReport Aid(string name = "AID #2", bool isLive = true) =>
        new()
        {
            Name = name,
            IsLive = isLive,
            Position = _position,
            Symbol = new AprsSymbol('/', 'A'),
        };

    // Objects

    [Fact]
    public void WritesAKilledObjectAsInApr101()
    {
        Assert.Equal(";LEADER   _092345z4903.50N/07201.75W>088/036", Information(Leader(isLive: false)));
    }

    [Fact]
    public void NewObjectsAreLive()
    {
        var report = new ObjectReport
        {
            Name = "LEADER",
            Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
            Position = _position,
            Symbol = new AprsSymbol('/', '>'),
        };

        Assert.True(report.IsLive);
        Assert.StartsWith(";LEADER   *092345z", Information(report), StringComparison.Ordinal);
    }

    [Fact]
    public void TheObjectDecodesToTheSameValues()
    {
        var decoded = Assert.IsType<ObjectReport>(_address.Create(Leader()).Payload);

        Assert.Equal("LEADER", decoded.Name);
        Assert.True(decoded.IsLive);
        Assert.Equal(AprsTimestamp.DayHourMinute(9, 23, 45), decoded.Timestamp);
        Assert.Equal(88, decoded.Course);
        Assert.Equal(36, decoded.SpeedKnots);
    }

    [Fact]
    public void AnObjectCanBeCompressedAndUseAnyTimestampButMonthDay()
    {
        var report = new ObjectReport
        {
            Name = "STORM",
            Timestamp = AprsTimestamp.HourMinuteSecond(23, 45, 17),
            Position = _position,
            Symbol = new AprsSymbol('\\', '@'),
            IsCompressed = true,
            Comment = "Moving NE",
        };

        var info = Information(report);

        Assert.StartsWith(";STORM    *234517h\\", info, StringComparison.Ordinal);
        Assert.EndsWith("Moving NE", info, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectNeedsATimestamp()
    {
        Rejects(
            new ObjectReport
            {
                Name = "LEADER",
                Position = _position,
                Symbol = new AprsSymbol('/', '>'),
            },
            "needs a timestamp"
        );
    }

    [Fact]
    public void AnObjectTimestampIsNotMonthDay()
    {
        var report = new ObjectReport
        {
            Name = "LEADER",
            Timestamp = AprsTimestamp.MonthDayHourMinute(10, 9, 23, 45),
            Position = _position,
            Symbol = new AprsSymbol('/', '>'),
        };

        Rejects(report, "not MMDDHHMM");
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGNAME")]
    [InlineData("TRAILING ")]
    [InlineData("CAFÉ")]
    [InlineData("TAB\tNAME")]
    public void RefusesObjectNamesThatCannotBeSent(string name)
    {
        Rejects(Leader(name), "object name");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("NINE CHRS")]
    [InlineData(" LEADING")]
    [InlineData("WITH*STAR")]
    public void AcceptsObjectNamesOfOneToNineCharacters(string name)
    {
        var decoded = (ObjectReport)_address.Create(Leader(name)).Payload!;

        Assert.Equal(name, decoded.Name);
    }

    // Items

    [Fact]
    public void WritesAnItemAsInApr101()
    {
        Assert.Equal(")AID #2!4903.50N/07201.75WA", Information(Aid()));
    }

    [Fact]
    public void WritesAKilledItem()
    {
        Assert.Equal(")AID #2_4903.50N/07201.75WA", Information(Aid(isLive: false)));
        Assert.False(((ItemReport)_address.Create(Aid(isLive: false)).Payload!).IsLive);
    }

    [Fact]
    public void NewItemsAreLive()
    {
        Assert.True(new ItemReport().IsLive);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("TENLETTERS")]
    [InlineData("BANG!")]
    [InlineData("UNDER_SC")]
    [InlineData("CAFÉ")]
    public void RefusesItemNamesThatCannotBeSent(string name)
    {
        Rejects(Aid(name), "item name");
    }

    [Fact]
    public void AnItemHasNoTimestamp()
    {
        var item = new ItemReport
        {
            Name = "AID #2",
            Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45),
            Position = _position,
            Symbol = new AprsSymbol('/', 'A'),
        };

        Rejects(item, "no timestamp");
    }

    [Fact]
    public void AnItemCarriesTheSameExtensionsAsAPosition()
    {
        var item = new ItemReport
        {
            Name = "RPTR",
            Position = _position,
            Symbol = new AprsSymbol('/', 'r'),
            PowerHeightGain = new PowerHeightGain(25, 20, 3, null),
            Comment = "146.940 -600",
        };

        Assert.Equal(")RPTR!4903.50N/07201.75WrPHG5130146.940 -600", Information(item));
    }

    // Parsed

    [Theory]
    [InlineData("N0CALL>APRS:;LEADER   _092345z4903.50N/07201.75W>088/036")]
    [InlineData("N0CALL>APRS:)AID #2!4903.50N/07201.75WA")]
    [InlineData("N0CALL>APRS:;444.900 V*111111z4903.5 N/07201.7 WrT100 R50m")]
    public void ParsedObjectsAndItemsRebuildByteForByte(string line)
    {
        var packet = AprsPacket.Parse(line);

        var rebuilt = AprsPacket.Create(packet.Source!, packet.Destination!, packet.Path!, packet.Payload!);

        Assert.Equal(packet.ToString(), rebuilt.ToString());
    }
}
