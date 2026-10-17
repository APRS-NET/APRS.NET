using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Kiss.Tests;

public sealed class KissSerialConnectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new();

    private static byte[] Kiss(string line) =>
        KissFrame.ForAx25(Ax25Frame.FromAprsPacket(AprsPacket.Parse(line)), 0).Encode();

    private KissSerialConnection Connection(Func<CancellationToken, Task<Stream>> open) =>
        new(open, new KissSerialOptions { PortName = "/dev/ttyUSB0", TimeProvider = _time });

    [Fact]
    public async Task ReadingCarriesOnAfterTheTncIsUnpluggedAndPluggedBackIn()
    {
        var before = new FakeTnc();
        var after = new FakeTnc();
        var attempts = 0;
        await using var connection = Connection(_ =>
            ++attempts switch
            {
                1 => Task.FromResult<Stream>(before),
                // While unplugged the device is gone, so opening it fails.
                2 or 3 => throw new FileNotFoundException("Could not find file '/dev/ttyUSB0'."),
                _ => Task.FromResult<Stream>(after),
            }
        );
        var states = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            lock (states)
            {
                states.Add(e);
            }
        };

        before.Send(Kiss("N0CALL>APRS:>before"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APRS:>before", await NextAsync(packets));

        // Unplugged: the read fails, then each attempt to open the port fails, waiting 2, 4 and 8 seconds.
        before.Fail(new IOException("Input/output error"));
        after.Send(Kiss("N0CALL>APRS:>after"));
        var next = NextAsync(packets);
        TimeSpan[] waits = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];
        for (var i = 0; i < waits.Length; i++)
        {
            await WaitUntil(() => Reconnecting(states).Count == i + 1);
            Assert.Equal(waits[i], Reconnecting(states)[i].RetryIn);
            Assert.False(connection.IsConnected);
            _time.Advance(waits[i]);
        }

        Assert.Equal("N0CALL>APRS:>after", await next);
        Assert.IsType<IOException>(Reconnecting(states)[0].Error);
        Assert.IsType<FileNotFoundException>(Reconnecting(states)[1].Error);
        Assert.True(connection.IsConnected);
        Assert.Equal(KissConnectionState.Connected, connection.State);
        Assert.Equal(1, connection.Reconnects);
    }

    [Fact]
    public async Task APortThatDotNetClosedWhenTheTncWasUnpluggedIsReconnected()
    {
        // On Linux an unplug makes .NET close the port, and the next read throws "The port is closed".
        var before = new FakeTnc();
        var after = new FakeTnc();
        var attempts = 0;
        await using var connection = Connection(_ =>
            Task.FromResult<Stream>(
                ++attempts == 1
                    ? new KissSerialConnection.SerialPortStream(before, before)
                    : new KissSerialConnection.SerialPortStream(after, after)
            )
        );
        var states = new List<KissConnectionStateChangedEventArgs>();
        connection.StateChanged += (_, e) =>
        {
            lock (states)
            {
                states.Add(e);
            }
        };

        before.Send(Kiss("N0CALL>APRS:>before"));
        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APRS:>before", await NextAsync(packets));

        before.Fail(new ObjectDisposedException(null, "The port is closed."));
        after.Send(Kiss("N0CALL>APRS:>after"));
        var next = NextAsync(packets);
        await WaitUntil(() => Reconnecting(states).Count == 1);
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal("N0CALL>APRS:>after", await next);
        var lost = Assert.IsType<IOException>(Reconnecting(states)[0].Error);
        Assert.IsType<ObjectDisposedException>(lost.InnerException);
    }

    [Fact]
    public async Task APortAnotherProgramHoldsIsTriedAgain()
    {
        var tnc = new FakeTnc();
        tnc.Send(Kiss("N0CALL>APRS:>free at last"));
        var attempts = 0;
        await using var connection = Connection(_ =>
            ++attempts == 1
                ? throw new UnauthorizedAccessException("Access to the port '/dev/ttyUSB0' is denied.")
                : Task.FromResult<Stream>(tnc)
        );

        await using var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = NextAsync(packets);
        await WaitUntil(() => connection.State == KissConnectionState.Reconnecting);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("N0CALL>APRS:>free at last", await next);
        Assert.Equal(0, connection.Reconnects);
    }

    [Fact]
    public async Task SendingGoesToTheOpenPort()
    {
        var tnc = new FakeTnc();
        await using var connection = new KissSerialConnection(
            _ => Task.FromResult<Stream>(tnc),
            new KissSerialOptions { PortName = "COM3", TransmitLimit = KissTransmitLimit.Off }
        );
        KissConnection asAny = connection;

        await using var frames = asAny
            .ReadFramesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var reading = frames.MoveNextAsync();
        await WaitUntil(() => connection.IsConnected);
        var packet = AprsPacket.Parse("N0CALL>APRS:>on air");
        await asAny.SendAsync(packet, TestContext.Current.CancellationToken);

        Assert.Equal(Kiss("N0CALL>APRS:>on air"), tnc.Written.ToArray());
        Assert.Equal("COM3", connection.PortName);
        tnc.End();
        await connection.DisposeAsync();
        await reading.AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task APortThatDoesNotExistIsRetried()
    {
        // The real port opener, on a name no machine has.
        var name = OperatingSystem.IsWindows() ? "COM250" : "/dev/aprsnet-no-such-port";
        await using var connection = new KissSerialConnection(
            new KissSerialOptions { PortName = name, TimeProvider = _time }
        );
        KissConnectionStateChangedEventArgs? lost = null;
        connection.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Reconnecting)
            {
                lost = e;
            }
        };

        var packets = connection
            .ReadPacketsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = packets.MoveNextAsync();
        await WaitUntil(() => lost is not null);
        await connection.DisposeAsync();
        Assert.False(await next.AsTask().WaitAsync(Wait, TestContext.Current.CancellationToken));
        await packets.DisposeAsync();

        // Linux reports a missing device as access denied, wrapping "No such file or directory".
        Assert.True(lost!.Error is IOException or UnauthorizedAccessException, lost.Error?.ToString());
        Assert.Equal(TimeSpan.FromSeconds(2), lost.RetryIn);
    }

    [Fact]
    public void TheDefaultsSuitAUsbTnc()
    {
        var options = new KissSerialOptions();
        Assert.Equal(9600, options.BaudRate);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ReconnectDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxReconnectDelay);

        var connection = new KissSerialConnection("/dev/ttyACM0", 57600);
        Assert.Equal("/dev/ttyACM0", connection.PortName);
        Assert.True(connection.TransmitLimit.IsOn);
        Assert.Equal(KissConnectionState.Idle, connection.State);
    }

    [Theory]
    [InlineData("", 9600, 2, 30)]
    [InlineData("COM3", 0, 2, 30)]
    [InlineData("COM3", 9600, 0, 30)]
    [InlineData("COM3", 9600, 10, 5)]
    public void UnusableSettingsAreRefused(string port, int baud, int delay, int maxDelay)
    {
        var options = new KissSerialOptions
        {
            PortName = port,
            BaudRate = baud,
            ReconnectDelay = TimeSpan.FromSeconds(delay),
            MaxReconnectDelay = TimeSpan.FromSeconds(maxDelay),
        };
        Assert.ThrowsAny<ArgumentException>(() => new KissSerialConnection(options));
    }

    [Fact]
    public void PortNamesAreListedInOrder()
    {
        var names = KissSerialConnection.GetPortNames();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(names.Distinct(), names);
    }

    private static List<KissConnectionStateChangedEventArgs> Reconnecting(
        List<KissConnectionStateChangedEventArgs> states
    )
    {
        lock (states)
        {
            return states.Where(e => e.State == KissConnectionState.Reconnecting).ToList();
        }
    }

    private static async Task<string> NextAsync(IAsyncEnumerator<AprsPacket> packets)
    {
        Assert.True(await packets.MoveNextAsync().AsTask().WaitAsync(Wait));
        return packets.Current.ToString();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting.");
            await Task.Delay(10);
        }
    }
}
