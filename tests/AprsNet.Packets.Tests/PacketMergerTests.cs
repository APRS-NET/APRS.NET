using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Packets.Tests;

public sealed class PacketMergerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    [Fact]
    public async Task APacketOnTwoSourcesComesOutOnceAsNewAndOnceAsADuplicate()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        var aprsIs = Channel.CreateUnbounded<AprsPacket>();
        var kiss = Channel.CreateUnbounded<AprsPacket>();
        _ = merger.Add("aprs-is", aprsIs.Reader.ReadAllAsync(Token));
        _ = merger.Add("kiss", kiss.Reader.ReadAllAsync(Token));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);

        // Heard on RF by an IGate and passed to APRS-IS, then heard directly by the local TNC.
        var first = _time.GetUtcNow();
        aprsIs.Writer.TryWrite(P("N0CALL>APRS,WIDE1-1,qAR,IGATE:>hello"));
        var fromAprsIs = await NextAsync(merged);
        _time.Advance(TimeSpan.FromSeconds(2));
        kiss.Writer.TryWrite(P("N0CALL>APRS,WIDE1-1:>hello"));
        var fromKiss = await NextAsync(merged);

        Assert.Equal("aprs-is", fromAprsIs.Source);
        Assert.False(fromAprsIs.IsDuplicate);
        Assert.Equal(first, fromAprsIs.ReceivedAt);
        Assert.Equal("kiss", fromKiss.Source);
        Assert.True(fromKiss.IsDuplicate);
        Assert.Equal(new MergedPacketOrigin("aprs-is", first), fromKiss.DuplicateOf);
        Assert.Equal("N0CALL>APRS,WIDE1-1:>hello", fromKiss.Packet.ToString());
    }

    [Fact]
    public async Task OutsideTheWindowBothCopiesAreNew()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        var aprsIs = Channel.CreateUnbounded<AprsPacket>();
        var kiss = Channel.CreateUnbounded<AprsPacket>();
        _ = merger.Add("aprs-is", aprsIs.Reader.ReadAllAsync(Token));
        _ = merger.Add("kiss", kiss.Reader.ReadAllAsync(Token));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);

        aprsIs.Writer.TryWrite(P("N0CALL>APRS:>beacon"));
        Assert.False((await NextAsync(merged)).IsDuplicate);
        _time.Advance(DuplicateFilter.DefaultWindow);
        kiss.Writer.TryWrite(P("N0CALL>APRS:>beacon"));
        var second = await NextAsync(merged);

        Assert.False(second.IsDuplicate);
        Assert.Equal("kiss", second.Source);
    }

    [Fact]
    public async Task UsesTheGivenDuplicateFilter()
    {
        var duplicates = new DuplicateFilter(TimeSpan.FromSeconds(5), timeProvider: _time);
        await using var merger = new PacketMerger(duplicates, timeProvider: _time);
        _ = merger.Add("a", Packets("N0CALL>APRS:>one"));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);
        await NextAsync(merged);

        Assert.True(duplicates.IsDuplicate(P("N0CALL>APRS:>one")));
    }

    [Fact]
    public async Task ASourceThatFailsEndsAloneAndReportsWhy()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        var healthy = Channel.CreateUnbounded<AprsPacket>();
        var failing = merger.Add("broken", Failing(new IOException("serial port gone"), Token));
        _ = merger.Add("healthy", healthy.Reader.ReadAllAsync(Token));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);

        var error = await Assert.ThrowsAsync<IOException>(() => failing.WaitAsync(Wait, Token));
        Assert.Equal("serial port gone", error.Message);
        Assert.Equal(["healthy"], merger.Sources);
        healthy.Writer.TryWrite(P("N0CALL>APRS:>still here"));
        Assert.Equal("healthy", (await NextAsync(merged)).Source);
    }

    [Fact]
    public async Task ASourceThatEndsIsForgottenAndItsNameCanBeUsedAgain()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        await merger.Add("once", Packets()).WaitAsync(Wait, Token);

        Assert.Empty(merger.Sources);
        _ = merger.Add("once", Packets("N0CALL>APRS:>again"));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);
        Assert.Equal("once", (await NextAsync(merged)).Source);
    }

    [Fact]
    public async Task RemovingASourceStopsIt()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        var source = Channel.CreateUnbounded<AprsPacket>();
        var reading = merger.Add("tnc", source.Reader.ReadAllAsync(Token));

        Assert.Equal(["tnc"], merger.Sources);
        Assert.True(await merger.RemoveAsync("tnc"));
        await reading.WaitAsync(Wait, Token);
        Assert.True(reading.IsCompletedSuccessfully);
        Assert.Empty(merger.Sources);
        Assert.False(await merger.RemoveAsync("tnc"));
    }

    [Fact]
    public async Task DisposingStopsTheSourcesAndEndsTheStream()
    {
        var merger = new PacketMerger(timeProvider: _time);
        var source = Channel.CreateUnbounded<AprsPacket>();
        var reading = merger.Add("tnc", source.Reader.ReadAllAsync(Token));
        await using var merged = merger.ReadAllAsync(Token).GetAsyncEnumerator(Token);
        source.Writer.TryWrite(P("N0CALL>APRS:>last"));
        Assert.Equal("N0CALL>APRS:>last", (await NextAsync(merged)).Packet.ToString());

        await merger.DisposeAsync();
        await reading.WaitAsync(Wait, Token);

        Assert.False(await merged.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = merger.Add("late", Packets());
        });
        await merger.DisposeAsync();
    }

    [Fact]
    public async Task RefusesBadSources()
    {
        await using var merger = new PacketMerger(timeProvider: _time);
        var source = Channel.CreateUnbounded<AprsPacket>();
        _ = merger.Add("tnc", source.Reader.ReadAllAsync(Token));

        Assert.Throws<ArgumentException>(() =>
        {
            _ = merger.Add("tnc", Packets());
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = merger.Add("", Packets());
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = merger.Add("x", null!);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketMerger(capacity: 0));
    }

    [Fact]
    public async Task ASlowReaderMakesTheSourcesWait()
    {
        await using var merger = new PacketMerger(capacity: 2, timeProvider: _time);
        var read = 0;
        _ = merger.Add("fast", Counting(10, () => read++));
        await WaitUntil(() => read >= 3);
        await Task.Delay(100, Token);

        // Two waiting in the channel and one held by the source as it waits to write.
        Assert.Equal(3, read);
        var all = new List<MergedPacket>();
        await foreach (var packet in merger.ReadAllAsync(Token))
        {
            all.Add(packet);
            if (all.Count == 10)
            {
                break;
            }
        }

        Assert.Equal(10, all.Count);
    }

    private static async IAsyncEnumerable<AprsPacket> Packets(params string[] lines)
    {
        await Task.Yield();
        foreach (var line in lines)
        {
            yield return P(line);
        }
    }

    private static async IAsyncEnumerable<AprsPacket> Counting(int count, Action onRead)
    {
        await Task.Yield();
        for (var i = 0; i < count; i++)
        {
            onRead();
            yield return P($"N0CALL>APRS:>{i}");
        }
    }

    private static async IAsyncEnumerable<AprsPacket> Failing(
        Exception error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw error;
#pragma warning disable CS0162 // An iterator needs a yield.
        yield break;
#pragma warning restore CS0162
    }

    private static async Task<MergedPacket> NextAsync(IAsyncEnumerator<MergedPacket> merged)
    {
        Assert.True(await merged.MoveNextAsync().AsTask().WaitAsync(Wait, Token));
        return merged.Current;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(10, Token);
        }
    }
}
