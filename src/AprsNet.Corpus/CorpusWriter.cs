using System.Text;

namespace AprsNet.Corpus;

/// <summary>Writes <see cref="CorpusLine"/>s to a stream in the corpus format: Latin-1, each line ending in <c>\n</c>.</summary>
public sealed class CorpusWriter : IDisposable, IAsyncDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>Creates a writer on <paramref name="stream"/>.</summary>
    /// <param name="stream">Where the lines go.</param>
    /// <param name="leaveOpen">Whether to leave the stream open when the writer is disposed.</param>
    /// <param name="autoFlush">Whether to flush after every line, so nothing written is lost if the program stops.</param>
    public CorpusWriter(Stream stream, bool leaveOpen = false, bool autoFlush = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _writer = new StreamWriter(stream, Encoding.Latin1, 4096, leaveOpen) { NewLine = "\n", AutoFlush = autoFlush };
    }

    /// <summary>Writes one line.</summary>
    public void Write(CorpusLine line) => _writer.WriteLine(Text(line));

    /// <summary>Writes one line.</summary>
    public Task WriteAsync(CorpusLine line, CancellationToken cancellationToken = default) =>
        _writer.WriteLineAsync(Text(line).AsMemory(), cancellationToken);

    /// <summary>Writes anything buffered to the stream.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default) => _writer.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    private static string Text(CorpusLine line) =>
        line.Packet is null
            ? throw new ArgumentException("The line is empty (default).", nameof(line))
            : line.ToString();
}
