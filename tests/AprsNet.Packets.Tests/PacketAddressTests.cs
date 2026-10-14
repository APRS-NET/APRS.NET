namespace AprsNet.Packets.Tests;

public class PacketAddressTests
{
    private static readonly PacketAddress _rf = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", "WIDE2-1"]);

    public static TheoryData<AprsPayload> Payloads =>
        [
            new PositionReport
            {
                Position = new AprsPosition(49.05833, -72.02917),
                Symbol = new AprsSymbol('/', '>'),
                Course = 88,
                SpeedKnots = 36,
                Comment = "On the road",
            },
            new StatusReport { Text = "Net Control" },
            new MessagePayload
            {
                Addressee = "WU2Z",
                Text = "Testing",
                MessageId = "003",
            },
        ];

    [Theory]
    [MemberData(nameof(Payloads))]
    public void CreateMatchesAprsPacketCreateWithTheSameParts(AprsPayload payload)
    {
        var expected = AprsPacket.Create(
            Callsign.Parse("N0CALL-9"),
            Callsign.Parse("APZ123"),
            [PathEntry.Parse("WIDE1-1"), PathEntry.Parse("WIDE2-1")],
            payload
        );

        Assert.Equal(expected.ToString(), _rf.Create(payload).ToString());
    }

    [Fact]
    public void TheConstructorTakesParsedParts()
    {
        var address = new PacketAddress(
            Callsign.Parse("N0CALL-9"),
            Callsign.Parse("APZ123"),
            [PathEntry.Parse("WIDE1-1"), PathEntry.Parse("WIDE2-1")]
        );

        Assert.Equal(_rf, address);
        Assert.Equal("N0CALL-9>APZ123,WIDE1-1,WIDE2-1", address.ToString());
    }

    [Theory]
    [InlineData("N0CALL*")]
    [InlineData("TOOLONGCALL-99")]
    [InlineData("N0CALL-123")]
    [InlineData("N0CALL-")]
    [InlineData("N0 CALL")]
    [InlineData("")]
    public void AnInvalidSourceIsRefusedWhenTheAddressIsMade(string source)
    {
        var ex = Assert.Throws<ArgumentException>(() => PacketAddress.Parse(source, "APZ123", []));

        Assert.StartsWith($"The source '{source}'", ex.Message, StringComparison.Ordinal);
        Assert.Equal("source", ex.ParamName);
    }

    [Theory]
    [InlineData("N0CALL")]
    [InlineData("N0CALL-15")]
    [InlineData("n0call-9")]
    [InlineData("N0CALL-L")]
    public void SourcesThatAprsIsAcceptsAreAllowed(string source)
    {
        Assert.Equal(source, PacketAddress.Parse(source, "APZ123", []).Source.Value);
    }

    [Theory]
    [InlineData("APZ123*")]
    [InlineData("APZ1234")]
    [InlineData("apz123")]
    [InlineData("APZ123-16")]
    [InlineData("APZ123-DS")]
    public void AnInvalidDestinationIsRefusedWhenTheAddressIsMade(string destination)
    {
        var ex = Assert.Throws<ArgumentException>(() => PacketAddress.Parse("N0CALL-9", destination, []));

        Assert.StartsWith($"The destination '{destination}'", ex.Message, StringComparison.Ordinal);
        Assert.Equal("destination", ex.ParamName);
    }

    [Theory]
    [InlineData("WIDE1-1,WIDE2-1")]
    [InlineData("WIDE 1")]
    [InlineData("")]
    [InlineData("TOOLONGALIAS")]
    public void AnInvalidPathEntryIsRefusedWhenTheAddressIsMade(string entry)
    {
        var ex = Assert.Throws<ArgumentException>(() => PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", entry]));

        Assert.StartsWith($"The path entry '{entry}'", ex.Message, StringComparison.Ordinal);
        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void ADefaultPathEntryIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => _rf with { Path = [default] });

        Assert.StartsWith("The path entry", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Path", ex.ParamName);
    }

    [Fact]
    public void APathOfMoreThanTenEntriesIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PacketAddress.Parse("N0CALL-9", "APZ123", Enumerable.Repeat("WIDE1-1", 11))
        );

        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void WithChecksTheNewParts()
    {
        var ex = Assert.Throws<ArgumentException>(() => _rf with { Destination = Callsign.Parse("APRS-DS") });

        Assert.StartsWith("The destination 'APRS-DS'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APathGivenToCreateIsForThatPacketOnly()
    {
        var packet = _rf.Create(new StatusReport { Text = "hi" }, [PathEntry.Parse("WIDE2-2")]);

        Assert.Equal("N0CALL-9>APZ123,WIDE2-2:>hi", packet.ToString());
        Assert.Equal(["WIDE1-1", "WIDE2-1"], _rf.Path.Select(p => p.ToString()));
        Assert.Equal("N0CALL-9>APZ123,WIDE1-1,WIDE2-1:>hi", _rf.Create(new StatusReport { Text = "hi" }).ToString());
    }

    [Fact]
    public void AnEmptyPathGivenToCreateSendsToAprsIsOnly()
    {
        Assert.Equal("N0CALL-9>APZ123:>hi", _rf.Create(new StatusReport { Text = "hi" }, []).ToString());
    }

    [Fact]
    public void WithAnEmptyPathMakesAnAprsIsAddress()
    {
        var aprsIs = _rf with { Path = [] };

        Assert.Equal("N0CALL-9>APZ123:>hi", aprsIs.Create(new StatusReport { Text = "hi" }).ToString());
        Assert.Empty(aprsIs.Create(new StatusReport { Text = "hi" }).Path!);
        Assert.Equal(2, _rf.Path.Count);
    }

    [Fact]
    public void ThePathCannotBeChangedThroughTheList()
    {
        var entries = new List<PathEntry> { PathEntry.Parse("WIDE1-1") };
        var address = new PacketAddress(Callsign.Parse("N0CALL-9"), Callsign.Parse("APZ123"), entries);
        entries.Add(PathEntry.Parse("WIDE2-1"));

        Assert.Single(address.Path);
        Assert.False(address.Path is IList<PathEntry> { IsReadOnly: false });
    }

    [Fact]
    public void AddressesWithTheSamePartsAreEqual()
    {
        var copy = PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", "WIDE2-1"]);

        Assert.Equal(_rf, copy);
        Assert.Equal(_rf.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(_rf, _rf with { Path = [] });
    }

    [Fact]
    public void AMicEPayloadIsRefused()
    {
        var micE = AprsPacket.Parse("N0CALL>S32U6T:`(_fn\"Oj/]Mobile").Payload;
        Assert.IsType<MicEReport>(micE);

        var ex = Assert.Throws<NotSupportedException>(() => _rf.Create(micE));
        Assert.Contains("Mic-E", ex.Message, StringComparison.Ordinal);
    }
}
