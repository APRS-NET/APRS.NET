using System.Text;
using AprsNet.Packets;

namespace AprsNet.Ax25.Tests;

public class Ax25FrameTests
{
    // N0CALL>APRS,WIDE1-1:>Hi, laid out by hand from the AX.25 2.2 address format: each callsign character shifted
    // left one bit and padded with spaces to six, then the SSID byte (flag, reserved bits 11, SSID, end of address).
    private static readonly byte[] _statusFrame =
    [
        0x82,
        0xA0,
        0xA4,
        0xA6,
        0x40,
        0x40,
        0xE0, // APRS, command bit set
        0x9C,
        0x60,
        0x86,
        0x82,
        0x98,
        0x98,
        0x60, // N0CALL
        0xAE,
        0x92,
        0x88,
        0x8A,
        0x62,
        0x40,
        0x63, // WIDE1-1, last address
        0x03,
        0xF0, // UI frame, no layer 3
        (byte)'>',
        (byte)'H',
        (byte)'i',
    ];

    [Fact]
    public void DecodesAnAprsUiFrame()
    {
        Assert.True(Ax25Frame.TryDecode(_statusFrame, out var frame, out _));

        Assert.True(frame.IsAprs);
        Assert.Equal("N0CALL", frame.Source.Callsign);
        Assert.Equal(new Ax25Address("WIDE1", 1, flag: false), frame.Digipeaters.Single());
        Assert.Equal("N0CALL>APRS,WIDE1-1:>Hi", frame.ToTnc2());
        Assert.Equal("Hi", ((StatusReport)frame.ToAprsPacket().Payload!).Text);
    }

    [Fact]
    public void EncodesTheSameBytes()
    {
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS,WIDE1-1:>Hi"));

        Assert.Equal(_statusFrame, frame.GetBytes());
    }

    [Fact]
    public void MarksOnlyTheLastRepeatedDigipeaterInText()
    {
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL-9>APRS,RELAY,WIDE1-1*,WIDE2-1:>Hi"));

        Assert.Equal([true, true, false], frame.Digipeaters.Select(d => d.Flag));
        Assert.Equal("N0CALL-9>APRS,RELAY,WIDE1-1*,WIDE2-1:>Hi", frame.ToTnc2());
    }

    [Fact]
    public void KeepsEveryByteOfTheInformationField()
    {
        // Mic-E data is not text: its bytes must cross unchanged, and the destination carries the latitude.
        var line = Encoding.Latin1.GetBytes("N0CALL-9>S32U6T,WIDE1-1:`(_fn\"Oj/]°ÿ");
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse(line));

        Assert.Equal(line, frame.ToAprsPacket().Raw.ToArray());
        Assert.IsType<MicEReport>(frame.ToAprsPacket().Payload);
    }

    [Theory]
    [InlineData("N0CALL>APRS,TCPIP*,qAC,T2TEST:>Hi", "q-construct")]
    [InlineData("N0CALL-123>APRS:>Hi", "source")]
    [InlineData("TOOLONG1>APRS:>Hi", "source")]
    [InlineData("n0call>APRS:>Hi", "source")]
    [InlineData("N0CALL>APRS,W1-16:>Hi", "path entry")]
    [InlineData("N0CALL>APRS,A,B,C,D,E,F,G,H,I:>Hi", "at most 8 digipeaters")]
    public void RefusesWhatAx25CannotCarry(string line, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsATruncatedFrame()
    {
        Assert.False(Ax25Frame.TryDecode(_statusFrame.AsSpan(0, 10), out _, out var error));
        Assert.Equal(Ax25FrameError.TooShort, error);
    }

    [Fact]
    public void ReportsAnAddressListThatDoesNotEnd()
    {
        var frame = (byte[])_statusFrame.Clone();
        frame[20] = 0x62; // clear the end-of-address bit on WIDE1-1

        Assert.False(Ax25Frame.TryDecode(frame, out _, out var error));
        Assert.Equal(Ax25FrameError.AddressNotTerminated, error); // the frame ends inside the next address
    }

    [Fact]
    public void ReportsAnInvalidCallsign()
    {
        var frame = (byte[])_statusFrame.Clone();
        frame[7] = (byte)('n' << 1);

        Assert.False(Ax25Frame.TryDecode(frame, out _, out var error));
        Assert.Equal(Ax25FrameError.InvalidAddress, error);
    }

    [Fact]
    public void ReadsANonAprsFrameWithoutCallingItAprs()
    {
        var frame = (byte[])_statusFrame.Clone();
        frame[21] = 0x01; // a receive-ready supervisory frame: no protocol identifier

        Assert.True(Ax25Frame.TryDecode(frame.AsSpan(0, 22), out var decoded, out _));
        Assert.False(decoded.IsAprs);
        Assert.Null(decoded.Protocol);
    }

    [Theory]
    [InlineData("N0CALL", "N0CALL", 0)]
    [InlineData("N0CALL-15", "N0CALL", 15)]
    [InlineData("WIDE2-2", "WIDE2", 2)]
    public void ParsesAddresses(string text, string callsign, int ssid)
    {
        Assert.True(Ax25Address.TryParse(text, out var address));
        Assert.Equal((callsign, ssid), (address.Callsign, address.Ssid));
        Assert.Equal(text, address.ToString());
    }

    [Theory]
    [InlineData("N0CALL-16")]
    [InlineData("N0CALL-")]
    [InlineData("N0CALL-01x")]
    [InlineData("SEVENCH")]
    [InlineData("qAR")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesAddressesAx25CannotCarry(string? text)
    {
        Assert.False(Ax25Address.TryParse(text, out _));
    }
}
