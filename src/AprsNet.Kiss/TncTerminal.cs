using System.Runtime.CompilerServices;
using System.Text;

namespace AprsNet.Kiss;

/// <summary>
/// A console on a TNC's own command line: send it commands and read what it says, as text. For a settings screen or
/// a command-line tool, to set up a TNC by hand that has no built-in <see cref="TncSetup"/>. The port must not be in
/// use by a <see cref="KissConnection"/> at the same time. Use <c>KissSerialConnection.OpenTerminal</c> for a serial
/// port.
/// </summary>
public sealed class TncTerminal : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;

    /// <summary>Creates a console on <paramref name="stream"/>, the TNC's port.</summary>
    /// <param name="stream">The port.</param>
    /// <param name="ownsStream">Whether disposing the console closes the port.</param>
    public TncTerminal(Stream stream, bool ownsStream = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _ownsStream = ownsStream;
    }

    /// <summary>Sends <paramref name="command"/> and a carriage return, as if typed at the TNC's prompt.</summary>
    /// <exception cref="ArgumentException">The command holds a character above U+00FF.</exception>
    public Task SendLineAsync(string command, CancellationToken cancellationToken = default) =>
        SendAsync(TncScriptStep.SendLine(command).Bytes, cancellationToken);

    /// <summary>Sends <paramref name="bytes"/> as they are, such as the KISS exit frame.</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="script"/> on the port and returns what the TNC said meanwhile; see
    /// <see cref="TncScript.RunAsync"/>. Don't run one while <see cref="ReadAsync"/> is reading.
    /// </summary>
    /// <exception cref="TncScriptException">An expected text didn't come in time.</exception>
    public Task<string> RunAsync(
        TncScript script,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(script);
        return script.RunAsync(_stream, timeProvider, cancellationToken);
    }

    /// <summary>
    /// Yields what the TNC sends, as Latin-1 text in the pieces it arrives in, until the port closes or
    /// <paramref name="cancellationToken"/> is cancelled. Bytes that aren't text, such as KISS frames, come through
    /// as they are.
    /// </summary>
    public async IAsyncEnumerable<string> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var buffer = new byte[1024];
        while (await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) is > 0 and var read)
        {
            yield return Encoding.Latin1.GetString(buffer, 0, read);
        }
    }

    /// <summary>Closes the port, unless the console was told not to own it.</summary>
    public ValueTask DisposeAsync() => _ownsStream ? _stream.DisposeAsync() : ValueTask.CompletedTask;
}
