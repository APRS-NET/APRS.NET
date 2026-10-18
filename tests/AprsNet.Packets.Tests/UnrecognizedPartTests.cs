namespace AprsNet.Packets.Tests;

public sealed class UnrecognizedPartTests
{
    private static string Part(AprsPacket packet, UnrecognizedPart part) =>
        packet.ToString().Substring(part.Offset, part.Length);

    [Fact]
    public void AnOrdinaryPacketHasNone()
    {
        string[] lines =
        [
            "N0CALL-9>APRS,WIDE1-1,WIDE2-1,qAR,KD9XYZ-10:!4903.50N/07201.75W-PHG5132 Test",
            "N0CALL-9>APRS,TCPIP*,qAC,T2TEST:=4903.50N/07201.75W>088/036/A=001234",
            "N0CALL>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900wRSW",
            "N0CALL>APRS,TCPIP*,qAC,T2TEST:_10090556c220s004g005t077r000p000P000h50b09900tU2k",
            "N0CALL>APRS,TCPIP*,qAC,SOME-LONG-LOGIN:>status", // after the q-construct, logins needn't be AX.25
            "N0CALL>APRS::KD9XYZ-9 :Hello{12",
            "N0CALL-1>AESPG4,TCPIP*,qAC,T2TEST:!1357.16N/10040.17E-ESP8266 IGATE", // text, not an extension
        ];

        foreach (var line in lines)
        {
            Assert.Empty(AprsPacket.Parse(line).Unrecognized);
        }
    }

    [Theory]
    [InlineData("N0CALL>APRS,TCPIP*,qAC,FIRST:;P-N0CALL *121201z3859.93NP09440.92W#PHG1-220DAPNET", "PHG1-22")]
    [InlineData("N0CALL>APRS,TCPIP*,qAC,T2TEST:!4903.50N/07201.75W>36O/012 Test", "36O/012")]
    public void ADataExtensionThatDoesNotDecodeIsUnknown(string line, string extension)
    {
        var packet = AprsPacket.Parse(line);

        var part = Assert.Single(packet.Unrecognized);
        Assert.Equal(UnrecognizedPartCode.UnknownDataExtension, part.Code);
        Assert.Equal(extension, Part(packet, part));
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
    }

    [Theory]
    [InlineData("N0CALL-9>APBM1D,KD9XYZA,DMR*,qAR,KD9XYZ:>status", "KD9XYZA")] // seven characters
    [InlineData("N0CALL-8>APLRG1,KD9XYZ-L*,qAO,KD9XYZ:>status", "KD9XYZ-L")] // a letter for the SSID
    [InlineData("N0CALL-8>APRS,KD9XYZ-16,qAR,KD9XYZ:>status", "KD9XYZ-16")]
    public void APathEntryNoDigipeaterCouldAddIsUnknown(string line, string entry)
    {
        var packet = AprsPacket.Parse(line);

        var part = Assert.Single(packet.Unrecognized);
        Assert.Equal(UnrecognizedPartCode.UnknownPathEntry, part.Code);
        Assert.Equal(entry, Part(packet, part));
    }

    [Theory]
    [InlineData("N0CALL-11>APOTW1,WIDE2-1,qAO,KD9XYZ:!3410.50N/11828.90W_207/002g004t070P000h36b10135V117OTW1", "V117")]
    [InlineData(
        "N0CALL-15>APRS,TCPIP*,qAC,T2BC:_10130719c343s000g001t050r000P7.000000000000001h99b10261",
        "P7.000000000000001"
    )]
    [InlineData("N0CALL-8>APOTRS,WIDE1-1,qAO,KD9XYZ:_01010000c...s...g...t072h19X241", "X241")]
    public void WeatherThatStopsAtAReadingItCannotReadSaysWhere(string line, string field)
    {
        var packet = AprsPacket.Parse(line);

        var part = Assert.Single(packet.Unrecognized);
        Assert.Equal(UnrecognizedPartCode.UnreadWeatherField, part.Code);
        Assert.Equal(field, Part(packet, part));
    }

    [Fact]
    public void UserDefinedDataIsNotDecoded()
    {
        var packet = AprsPacket.Parse("N0CALL-4>APECAN,WIDE2-1,qAO,KD9XYZ:{{BAA{aL)?f,WmA5aAARdbHC");

        var part = Assert.Single(packet.Unrecognized);
        Assert.Equal(UnrecognizedPartCode.UserDefinedData, part.Code);
        Assert.Equal("{{BAA{aL)?f,WmA5aAARdbHC", Part(packet, part));
    }

    [Fact]
    public void AThirdPartyPacketKeepsItsOwnParts()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS,TCPIP*,qAC,T2TEST:}KD9XYZ>APRS,TCPIP,N0CALL*:{{data");

        Assert.Empty(packet.Unrecognized);
        var inner = Assert.IsType<ThirdPartyPayload>(packet.Payload).Packet;
        Assert.Equal(UnrecognizedPartCode.UserDefinedData, Assert.Single(inner.Unrecognized).Code);
    }

    [Fact]
    public void EveryCodeHasADescription()
    {
        foreach (var code in Enum.GetValues<UnrecognizedPartCode>())
        {
            Assert.NotEqual(code.ToString(), code.Describe());
        }
    }
}
