using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

public class KissTests
{
    [Fact]
    public void EscapesFrameEndAndEscapeBytesAsInTheKissSpecification()
    {
        var frame = new KissFrame(0, KissCommand.Data, new byte[] { 0x01, 0xC0, 0x02, 0xDB, 0x03 });

        Assert.Equal([0xC0, 0x00, 0x01, 0xDB, 0xDC, 0x02, 0xDB, 0xDD, 0x03, 0xC0], frame.Encode());
    }

    [Fact]
    public void PutsThePortInTheHighNibble()
    {
        Assert.Equal([0xC0, 0x31, 0x32, 0xC0], new KissFrame(3, KissCommand.TxDelay, new byte[] { 0x32 }).Encode());
        Assert.Equal([0xC0, 0xFF, 0xC0], new KissFrame(0, KissCommand.Return, ReadOnlyMemory<byte>.Empty).Encode());
    }

    [Fact]
    public void RefusesAPortAbove15()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissFrame(16, KissCommand.Data, ReadOnlyMemory<byte>.Empty).Encode()
        );
    }

    [Fact]
    public void DecodesWhatItEncodes()
    {
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); // every byte, both specials included
        var wire = new KissFrame(2, KissCommand.Data, payload).Encode();

        var frame = Assert.Single(new KissDecoder().Decode(wire));

        Assert.Equal((2, KissCommand.Data), (frame.Port, frame.Command));
        Assert.Equal(payload, frame.Payload.ToArray());
    }

    [Fact]
    public void ReassemblesFramesSplitAtEveryPossiblePoint()
    {
        var first = new KissFrame(0, KissCommand.Data, new byte[] { 0xC0, 0x41, 0xDB }).Encode();
        var second = new KissFrame(1, KissCommand.Data, new byte[] { 0x42 }).Encode();
        byte[] stream = [.. first, .. second];

        for (var split = 0; split <= stream.Length; split++)
        {
            var decoder = new KissDecoder();
            var frames = decoder.Decode(stream.AsSpan(0, split)).Concat(decoder.Decode(stream.AsSpan(split))).ToList();

            Assert.Equal(2, frames.Count);
            Assert.Equal([0xC0, 0x41, 0xDB], frames[0].Payload.ToArray());
            Assert.Equal(1, frames[1].Port);
        }
    }

    [Fact]
    public void ReadsBackToBackFramesSharingAFrameEnd()
    {
        // Many TNCs send one FEND between frames instead of two.
        byte[] stream = [0xC0, 0x00, 0x41, 0xC0, 0x00, 0x42, 0xC0];

        var frames = new KissDecoder().Decode(stream);

        Assert.Equal(["A", "B"], frames.Select(f => ((char)f.Payload.Span[0]).ToString()));
    }

    [Fact]
    public void IgnoresNoiseBeforeTheFirstFrameAndEmptyFrames()
    {
        byte[] stream = [0x41, 0x42, 0xDB, 0xC0, 0xC0, 0xC0, 0x00, 0x43, 0xC0];

        var decoder = new KissDecoder();
        var frame = Assert.Single(decoder.Decode(stream));

        Assert.Equal([0x43], frame.Payload.ToArray());
        Assert.Equal(0, decoder.DroppedFrames);
    }

    [Fact]
    public void DropsAFrameWithAnInvalidEscapeAndCarriesOn()
    {
        byte[] stream = [0xC0, 0x00, 0x41, 0xDB, 0x99, 0x42, 0xC0, 0x00, 0x43, 0xC0];

        var decoder = new KissDecoder();
        var frame = Assert.Single(decoder.Decode(stream));

        Assert.Equal([0x43], frame.Payload.ToArray());
        Assert.Equal(1, decoder.DroppedFrames);
    }

    [Fact]
    public void DropsAFrameLongerThanTheLimitWithoutBufferingIt()
    {
        var decoder = new KissDecoder(maxFrameLength: 8);
        byte[] tooLong = [0xC0, 0x00, .. Enumerable.Repeat((byte)0x41, 100), 0xC0];
        byte[] fits = [0x00, 0x42, 0xC0];

        Assert.Empty(decoder.Decode(tooLong));
        Assert.Equal([0x42], Assert.Single(decoder.Decode(fits)).Payload.ToArray());
        Assert.Equal(1, decoder.DroppedFrames);
    }

    [Fact]
    public void CarriesAnAprsPacketThroughKissAndAx25()
    {
        var packet = AprsPacket.Parse("N0CALL-9>APRS,WIDE1-1:!4903.50N/07201.75W>Testing");
        var wire = KissFrame.ForAx25(Ax25Frame.FromAprsPacket(packet)).Encode();

        var received = Assert.Single(new KissDecoder().Decode(wire));

        Assert.True(received.TryGetAx25(out var frame));
        Assert.Equal(packet.ToString(), frame!.ToAprsPacket().ToString());
    }

    [Fact]
    public void OnlyDataFramesCarryAx25()
    {
        Assert.False(new KissFrame(0, KissCommand.TxDelay, new byte[] { 0x32 }).TryGetAx25(out _));
        Assert.False(new KissFrame(0, KissCommand.Data, new byte[] { 0x01, 0x02 }).TryGetAx25(out _));
    }
}
