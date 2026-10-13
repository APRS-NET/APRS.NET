using System.Threading.Channels;
// ReSharper disable once RedundantUsingDirective
using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>In-memory transport: the test pushes server lines in and reads what the client wrote.</summary>
internal sealed class FakeAprsTransport : IAprsTransport
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private int _waitingReads;

    public List<string> Written { get; } = [];

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool Connected { get; private set; }

    // Read by the client tests; this file is also compiled into the CLI tests, which do not use every member.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool Disposed { get; private set; }

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        Connected = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_incoming.Reader.TryRead(out var line))
                return line;

            // Nothing queued: the client is now blocked on the "network", with its read deadline already armed.
            Interlocked.Increment(ref _waitingReads);
            try
            {
                if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
                    return null;
            }
            finally
            {
                Interlocked.Decrement(ref _waitingReads);
            }
        }
    }

    /// <summary>
    /// Waits until the client has read everything queued and is blocked waiting for more, so a test can then advance a
    /// fake clock knowing the client's read deadline is running.
    /// </summary>
    // ReSharper disable once UnusedMember.Global (used by the client tests; see Disposed)
    public async Task WaitUntilReadingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Volatile.Read(ref _waitingReads) == 0 || _incoming.Reader.Count > 0)
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (Written)
        {
            Written.Add(line);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Queues a line the "server" sends.</summary>
    public void Server(string line)
    {
        _incoming.Writer.TryWrite(line);
    }

    /// <summary>The "server" closes the connection.</summary>
    public void Close()
    {
        _incoming.Writer.TryComplete();
    }

    /// <summary>The "server" connection fails with an error.</summary>
    public void Fail(Exception exception)
    {
        _incoming.Writer.TryComplete(exception);
    }
}
