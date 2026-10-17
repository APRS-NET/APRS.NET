using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// A TNC behind a USB chip powered from the computer leaves its port open when switched off; only silence shows it.
/// </summary>
public sealed class KissQuietWarningTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);
    private readonly FakeTimeProvider _time = new();
    private readonly List<(KissWarning Warning, bool IsActive)> _warnings = [];

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    private KissSerialConnection Connection(Func<CancellationToken, Task<Stream>> open, TimeSpan? quiet = null)
    {
        var connection = new KissSerialConnection(
            open,
            new KissSerialOptions
            {
                PortName = "/dev/ttyUSB0",
                TimeProvider = _time,
                QuietWarning = quiet ?? Quiet,
            }
        );
        connection.Warning += (_, e) =>
        {
            lock (_warnings)
            {
                _warnings.Add((e.Warning, e.IsActive));
            }
        };
        return connection;
    }

    private List<(KissWarning, bool)> Warnings()
    {
        lock (_warnings)
        {
            return [.. _warnings];
        }
    }

    [Fact]
    public async Task ATncThatSendsNothingForTheQuietTimeIsReportedAndClearedWhenItSendsAgain()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        tnc.Send(Kiss("N0CALL>APRS:>first"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await packets.MoveNextAsync());

        var next = packets.MoveNextAsync().AsTask();
        _time.Advance(Quiet - TimeSpan.FromSeconds(1));
        Assert.Empty(Warnings());
        Assert.False(connection.IsQuiet);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([(KissWarning.Quiet, true)], Warnings());
        Assert.True(connection.IsQuiet);

        // Still quiet: it isn't reported again.
        _time.Advance(Quiet * 3);
        Assert.Single(Warnings());

        tnc.Send(Kiss("N0CALL>APRS:>back"));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal([(KissWarning.Quiet, true), (KissWarning.Quiet, false)], Warnings());
        Assert.False(connection.IsQuiet);
        Assert.Contains(
            "is it switched on?",
            new KissWarningEventArgs(KissWarning.Quiet, true).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task EachArrivalStartsTheQuietTimeAgain()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
        {
            var next = packets.MoveNextAsync().AsTask();
            _time.Advance(Quiet - TimeSpan.FromSeconds(10));
            tnc.Send(Kiss($"N0CALL>APRS:>packet {i}"));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }

        Assert.Empty(Warnings());
    }

    [Fact]
    public async Task TextCountsAsSomethingSoATncInTheWrongModeIsNotQuiet()
    {
        var tnc = new FakeTnc();
        await using var connection = Connection(_ => Task.FromResult<Stream>(tnc));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var packets = connection.ReadPacketsAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.IsConnected);

        // Half the quiet time passes, then text arrives; the TextNotKiss warning shows it was read and timed.
        _time.Advance(Quiet / 2);
        tnc.Send(
            System.Text.Encoding.ASCII.GetBytes(
                string.Concat(
                    Enumerable
                        .Range(0, 3)
                        .Select(i => $"N0CALL-{i}>APRS:>text, not KISS, from a TNC in its APRS mode\r\n")
                )
            )
        );
        await WaitUntil(() => Warnings().Contains((KissWarning.TextNotKiss, true)));

        // More than the quiet time since connecting, but less since the text.
        _time.Advance(Quiet / 2 + TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(Warnings(), w => w.Item1 == KissWarning.Quiet);
        Assert.Contains((KissWarning.TextNotKiss, true), Warnings());
        Assert.False(next.IsCompleted);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task ItIsOffUnlessSet()
    {
        var tnc = new FakeTnc();
        await using var connection = new KissSerialConnection(
            _ => Task.FromResult<Stream>(tnc),
            new KissSerialOptions { PortName = "/dev/ttyUSB0", TimeProvider = _time }
        );
        connection.Warning += (_, e) => _warnings.Add((e.Warning, e.IsActive));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var packets = connection.ReadPacketsAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var next = packets.MoveNextAsync().AsTask();
        await WaitUntil(() => connection.IsConnected);

        _time.Advance(TimeSpan.FromDays(1));

        Assert.Null(connection.QuietWarning);
        Assert.Empty(Warnings());
        Assert.False(next.IsCompleted);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public void AQuietTimeMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KissSerialOptions { PortName = "/dev/ttyUSB0", QuietWarning = TimeSpan.Zero }.Validate()
        );

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
