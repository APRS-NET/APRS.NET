using System.Text;

namespace AprsNet.Corpus.Tests;

public sealed class CorpusLineTests
{
    private static readonly DateTimeOffset _at = new(2026, 10, 12, 22, 57, 33, 910, TimeSpan.Zero);

    // A Mic-E packet from the Denver capture with bytes that aren't valid UTF-8: 0xFF padding, and 0xC3 0x28.
    private static readonly byte[] _micE =
    [
        .. "QA0ABT-7>S9SSQT,WIDE1-1,WIDE2-1,qAR,Q0AAM:`pB[m!Tk/]\"HV}145.190MHz -060"u8,
        0xFF,
        0xFF,
        0xC3,
        0x28,
        0x80,
        .. "="u8,
    ];

    [Fact]
    public void ALineIsATimestampASpaceAndThePacket()
    {
        var line = new CorpusLine(_at, "N0CALL>APRS,TCPIP*:>Hello");

        Assert.Equal("2026-10-12T22:57:33.910Z N0CALL>APRS,TCPIP*:>Hello", line.ToString());
        Assert.Equal(line, CorpusLine.Parse(line.ToString()));
    }

    [Fact]
    public void TheTimeIsKeptInUtcToTheMillisecond()
    {
        var local = new DateTimeOffset(2026, 10, 12, 17, 57, 33, TimeSpan.FromHours(-5)).AddTicks(9_109_999);

        var line = new CorpusLine(local, "N0CALL>APRS:>x");

        Assert.Equal(_at, line.ReceivedAt);
        Assert.Equal(TimeSpan.Zero, line.ReceivedAt.Offset);
        Assert.Equal(line, CorpusLine.Parse(line.ToString())); // nothing lost on the way through the text
    }

    [Fact]
    public void AMicEPacketThatIsNotUtf8IsKeptByteForByte()
    {
        var line = new CorpusLine(_at, _micE);

        Assert.Equal(_micE, line.GetPacketBytes());
        Assert.Equal(_micE.Length, line.Packet.Length);
        Assert.Equal(_micE, CorpusLine.Parse(line.ToString()).GetPacketBytes());
    }

    [Fact]
    public void AMicEPacketWritesAndReadsBackUnchanged()
    {
        using var stream = new MemoryStream();
        using (var writer = new CorpusWriter(stream, leaveOpen: true))
        {
            writer.Write(new CorpusLine(_at, _micE));
        }

        byte[] expected = [.. "2026-10-12T22:57:33.910Z "u8, .. _micE, (byte)'\n'];
        Assert.Equal(expected, stream.ToArray());

        stream.Position = 0;
        var read = Assert.Single(CorpusFile.ReadLines(stream));
        Assert.Equal(_micE, read.GetPacketBytes());
        Assert.Equal(_at, read.ReceivedAt);
    }

    [Theory]
    [InlineData("N0CALL>APRS:>one\ntwo")]
    [InlineData("N0CALL>APRS:>one\rtwo")]
    [InlineData("N0CALL>APRS:>café €")] // € is not one byte; é is
    public void APacketThatCannotBeOneLineOfBytesIsRefused(string packet)
    {
        Assert.Throws<ArgumentException>(() => new CorpusLine(_at, packet));
    }

    [Theory]
    [InlineData("")]
    [InlineData("N0CALL>APRS:>no timestamp")]
    [InlineData("2026-10-12T22:57:33.910Z")]
    [InlineData("2026-10-12T22:57:33.910Z ")]
    [InlineData("2026-10-12T22:57:33Z N0CALL>APRS:>no milliseconds")]
    [InlineData("2026-10-12 22:57:33.910Z N0CALL>APRS:>no T")]
    [InlineData("2026-13-12T22:57:33.910Z N0CALL>APRS:>month 13")]
    public void LinesNotInTheFormatAreRefused(string text)
    {
        Assert.False(CorpusLine.TryParse(text, out _));
        Assert.Throws<FormatException>(() => CorpusLine.Parse(text));
    }

    [Fact]
    public void TheEncodingIsLatin1()
    {
        Assert.Equal(Encoding.Latin1.WebName, CorpusFile.Encoding.WebName);
    }
}
