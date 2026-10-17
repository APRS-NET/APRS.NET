using System.Text;
using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>A TNC that isn't in KISS mode sends text lines; a KISS reader says so instead of dropping them silently.</summary>
public sealed class KissTextDetectionTests
{
    /// <summary>What a radio's built-in TNC sends in its APRS mode: TNC-2 monitor lines ending in CR LF.</summary>
    private static readonly byte[] TncText = Encoding.Latin1.GetBytes(
        "QA0AAA-7>APDR16,XAAAAA,WIDE1,QA0AA-1,WIDE2*:=3906.66N/09505.61W$359/028/A=000793 https://aprsdroid.org/\r\n"
            + "XAAAAB>APMI01,XAAAAA,QA0AA-1,WIDE2*:;146.91-KC*111111z3859.27N/09440.26WrT088 Fusion\r\n"
            + "QA0AAB-9>S8UY5V,QA0AA-1,WIDE1*,WIDE2-1:`z8.l :=/'\"7:}|)o%Z'c|!wdR!|3\r\n"
    );

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    [Fact]
    public void TextFromATncNotInKissModeIsSpottedAndCounted()
    {
        var decoder = new KissDecoder();

        Assert.Empty(decoder.Decode(TncText));
        Assert.True(decoder.LooksLikeText);
        Assert.Equal(TncText.Length, decoder.DiscardedBytes);
    }

    [Fact]
    public void AGoodFrameClearsIt()
    {
        var decoder = new KissDecoder();
        decoder.Decode(TncText);

        Assert.Single(decoder.Decode(Kiss("N0CALL>APRS:>back in KISS mode")));
        Assert.False(decoder.LooksLikeText);
    }

    [Fact]
    public void TextAfterKissFramesIsSpottedToo()
    {
        // A TNC that came back from a power cut with KISS off: frames, then text with no FEND to end it.
        var decoder = new KissDecoder();
        Assert.Single(decoder.Decode(Kiss("N0CALL>APRS:>still in KISS mode")));

        decoder.Decode(TncText);
        Assert.True(decoder.LooksLikeText);
    }

    [Fact]
    public void KissFramesWithLongTextAreNotTakenForText()
    {
        var decoder = new KissDecoder();
        var comment = new string('x', 200);
        var frames = Enumerable
            .Range(0, 20)
            .SelectMany(i => Kiss($"N0CALL-{i % 16}>APRS,WIDE1-1:>Status {i} {comment}"))
            .ToArray();

        Assert.Equal(20, decoder.Decode(frames).Count);
        Assert.False(decoder.LooksLikeText);
        Assert.Equal(0, decoder.DiscardedBytes);
    }

    [Fact]
    public void ALittleNoiseIsNotText()
    {
        var decoder = new KissDecoder();
        decoder.Decode(Encoding.ASCII.GetBytes("\r\ncmd:\r\n"));
        Assert.False(decoder.LooksLikeText);
    }

    [Fact]
    public async Task TheStreamClientWarnsWhenTextStartsAndWhenItStops()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc);
        var warnings = new List<KissWarningEventArgs>();
        client.Warning += (_, e) => warnings.Add(e);

        tnc.Send(TncText);
        tnc.Send(Kiss("N0CALL>APRS:>KISS again"));
        tnc.End();
        var packets = await client
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(">KISS again", Assert.Single(packets).Information.Span.ToArray().Select(b => (char)b).ToArray());
        Assert.Equal(
            [(KissWarning.TextNotKiss, true), (KissWarning.TextNotKiss, false)],
            [.. warnings.Select(w => (w.Warning, w.IsActive))]
        );
        Assert.Contains("is it in KISS mode?", warnings[0].Message, StringComparison.Ordinal);
        Assert.Equal(TncText.Length, client.DiscardedBytes);
    }

    [Fact]
    public async Task TextAndAFrameInOneReadStillWarn()
    {
        var tnc = new FakeTnc();
        await using var client = new KissStreamClient(tnc);
        var warnings = new List<bool>();
        client.Warning += (_, e) => warnings.Add(e.IsActive);

        tnc.Send([.. TncText, .. Kiss("N0CALL>APRS:>KISS again")]);
        tnc.End();
        await client
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([true, false], warnings);
    }

    [Fact]
    public async Task TheConnectionWarnsOnceAcrossReconnectsAndClearsWhenFramesArrive()
    {
        var time = new FakeTimeProvider();
        var first = new FakeTnc();
        var second = new FakeTnc();
        var attempts = 0;
        await using var connection = new KissSerialConnection(
            _ => Task.FromResult<Stream>(++attempts == 1 ? first : second),
            new KissSerialOptions { PortName = "/dev/ttyUSB0", TimeProvider = time }
        );
        var warnings = new List<bool>();
        connection.Warning += (_, e) =>
        {
            lock (warnings)
            {
                warnings.Add(e.IsActive);
            }
        };

        // Text on the first connection, then the link drops; text again on the second, then frames.
        first.Send(TncText);
        first.Fail(new IOException("Input/output error"));
        second.Send(TncText);
        second.Send(Kiss("N0CALL>APRS:>KISS at last"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.State == KissConnectionState.Reconnecting);
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        lock (warnings)
        {
            Assert.Equal([true, false], warnings);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
