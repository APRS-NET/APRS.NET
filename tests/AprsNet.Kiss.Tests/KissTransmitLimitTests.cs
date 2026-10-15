using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class KissTransmitLimitTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABurstGoesAtOnceThenOneFramePerInterval()
    {
        var limit = new KissTransmitLimit(2, TimeSpan.FromSeconds(10), _time);

        await limit.WaitAsync(Token).AsTask().WaitAsync(Wait, Token);
        await limit.WaitAsync(Token).AsTask().WaitAsync(Wait, Token);
        var third = limit.WaitAsync(Token).AsTask();
        await Task.Delay(50, Token);
        Assert.False(third.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(50, Token);
        Assert.False(third.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));
        await third.WaitAsync(Wait, Token);
    }

    [Fact]
    public async Task AQuietSpellRefillsTheBurstButNoMore()
    {
        var limit = new KissTransmitLimit(2, TimeSpan.FromSeconds(10), _time);
        _time.Advance(TimeSpan.FromHours(1));

        await limit.WaitAsync(Token).AsTask().WaitAsync(Wait, Token);
        await limit.WaitAsync(Token).AsTask().WaitAsync(Wait, Token);
        var third = limit.WaitAsync(Token).AsTask();
        await Task.Delay(50, Token);
        Assert.False(third.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(10));
        await third.WaitAsync(Wait, Token);
    }

    [Fact]
    public void OffNeverWaits()
    {
        Assert.False(KissTransmitLimit.Off.IsOn);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(KissTransmitLimit.Off.WaitAsync(Token).AsTask().IsCompletedSuccessfully);
        }
    }

    [Fact]
    public async Task ACancelledWaitDoesNotUseATurn()
    {
        var limit = new KissTransmitLimit(1, TimeSpan.FromSeconds(10), _time);
        await limit.WaitAsync(Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancelled = limit.WaitAsync(cancel.Token).AsTask();
        var next = limit.WaitAsync(Token).AsTask();

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Wait, Token));
        _time.Advance(TimeSpan.FromSeconds(10));
        await next.WaitAsync(Wait, Token);
    }

    [Fact]
    public void HasSafeDefaults()
    {
        var limit = KissTransmitLimit.Default;

        Assert.Equal(3, limit.Burst);
        Assert.Equal(TimeSpan.FromSeconds(10), limit.Interval);
        Assert.True(limit.IsOn);
        Assert.NotSame(limit, KissTransmitLimit.Default);
        Assert.True(new KissStreamClient(new MemoryStream()).TransmitLimit.IsOn);
        Assert.True(new KissTcpClient("tnc").TransmitLimit.IsOn);
        Assert.True(new KissTcpConnection("tnc").TransmitLimit.IsOn);
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTransmitLimit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KissTransmitLimit(interval: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task AStreamClientWaitsForTheLimitBeforeWriting()
    {
        var limit = new KissTransmitLimit(1, TimeSpan.FromSeconds(10), _time);
        var stream = new MemoryStream();
        await using var client = new KissStreamClient(stream, transmitLimit: limit);

        await client.SendAsync(AprsPacket.Parse("N0CALL>APRS:>one"), Token);
        var second = client.SendAsync(AprsPacket.Parse("N0CALL>APRS:>two"), Token).AsTask();
        await Task.Delay(50, Token);
        Assert.False(second.IsCompleted);
        Assert.Single(new KissDecoder().Decode(stream.ToArray()));
        _time.Advance(TimeSpan.FromSeconds(10));
        await second.WaitAsync(Wait, Token);

        Assert.Equal(2, new KissDecoder().Decode(stream.ToArray()).Count);
    }

    [Fact]
    public async Task ClientsSharingALimitTakeTurns()
    {
        var limit = new KissTransmitLimit(1, TimeSpan.FromSeconds(10), _time);
        await using var first = new KissStreamClient(new MemoryStream(), transmitLimit: limit);
        await using var second = new KissStreamClient(new MemoryStream(), transmitLimit: limit);

        await first.SendAsync(AprsPacket.Parse("N0CALL>APRS:>one"), Token);
        var waiting = second.SendAsync(AprsPacket.Parse("N0CALL>APRS:>two"), Token).AsTask();
        await Task.Delay(50, Token);

        Assert.False(waiting.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(10));
        await waiting.WaitAsync(Wait, Token);
    }
}
