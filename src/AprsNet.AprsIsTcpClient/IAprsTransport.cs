namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// A line-oriented link to an APRS source. TCP to APRS-IS implements it here; KISS and WebSocket sources can later
/// reuse the same client pipeline. Lines are decoded as Latin-1 so every byte maps to one character (Mic-E data is not valid UTF-8).
/// </summary>
public interface IAprsTransport : IAsyncDisposable
{
    /// <summary>Opens the connection.</summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Reads the next line without its terminator, or null when the peer closed the connection.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>Writes one line and its terminator.</summary>
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}
