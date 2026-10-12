using System.Threading.Channels;
// ReSharper disable once RedundantUsingDirective
using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>In-memory transport: the test pushes server lines in and reads what the client wrote.</summary>
internal sealed class FakeAprsTransport : IAprsTransport
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();

    public List<string> Written { get; } = [];

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool Connected { get; private set; }

    public bool Disposed { get; private set; }

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        Connected = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (!await _incoming.Reader.WaitToReadAsync(cancellationToken)) return null;

        return _incoming.Reader.TryRead(out var line) ? line : null;
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
