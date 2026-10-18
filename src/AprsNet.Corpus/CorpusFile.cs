using System.Runtime.CompilerServices;
using System.Text;

namespace AprsNet.Corpus;

/// <summary>
/// Reads and writes corpus files: one <see cref="CorpusLine"/> per line, each ending in <c>\n</c>, in Latin-1 so every
/// byte of a packet round-trips. The same format as <c>aprscli listen --capture</c> and <c>tests/corpus</c>.
/// </summary>
public static class CorpusFile
{
    /// <summary>The files' encoding: Latin-1, one byte per character.</summary>
    public static Encoding Encoding => Encoding.Latin1;

    /// <summary>
    /// Reads the lines of the corpus file at <paramref name="path"/>, skipping empty lines. A capture still being
    /// written (<see cref="OpenAppend"/>) can be read meanwhile.
    /// </summary>
    /// <exception cref="FormatException">A line is not in the corpus format; the message gives its line number.</exception>
    public static IEnumerable<CorpusLine> ReadLines(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadLines(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), leaveOpen: false);
    }

    /// <summary>Reads corpus lines from <paramref name="stream"/>, skipping empty lines.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="leaveOpen">Whether to leave the stream open once the lines are read.</param>
    /// <exception cref="FormatException">A line is not in the corpus format; the message gives its line number.</exception>
    public static IEnumerable<CorpusLine> ReadLines(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Read(stream, leaveOpen);

        static IEnumerable<CorpusLine> Read(Stream stream, bool leaveOpen)
        {
            using var reader = new StreamReader(stream, Encoding.Latin1, false, 4096, leaveOpen);
            var number = 0;
            while (reader.ReadLine() is { } text)
            {
                number++;
                if (text.Length > 0)
                {
                    yield return Line(text, number);
                }
            }
        }
    }

    /// <summary>Reads corpus lines from <paramref name="stream"/> as they arrive, skipping empty lines.</summary>
    /// <exception cref="FormatException">A line is not in the corpus format; the message gives its line number.</exception>
    public static async IAsyncEnumerable<CorpusLine> ReadLinesAsync(
        Stream stream,
        bool leaveOpen = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.Latin1, false, 4096, leaveOpen);
        var number = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } text)
        {
            number++;
            if (text.Length > 0)
            {
                yield return Line(text, number);
            }
        }
    }

    /// <summary>Writes <paramref name="lines"/> to the file at <paramref name="path"/>, replacing it or appending.</summary>
    public static void WriteLines(string path, IEnumerable<CorpusLine> lines, bool append = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(lines);
        using var writer = new CorpusWriter(
            new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read)
        );
        foreach (var line in lines)
        {
            writer.Write(line);
        }
    }

    /// <summary>
    /// Opens the file at <paramref name="path"/> to add lines at its end, creating it and its folder if needed. Each
    /// line is flushed as it is written, so a capture that stops suddenly loses nothing already written, and other
    /// programs may read the file meanwhile.
    /// </summary>
    public static CorpusWriter OpenAppend(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        return new CorpusWriter(
            new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            autoFlush: true
        );
    }

    private static CorpusLine Line(string text, int number) =>
        CorpusLine.TryParse(text, out var line)
            ? line
            : throw new FormatException(
                $"Line {number} is not '<UTC timestamp> <packet>' ({CorpusLine.TimestampFormat}): {text}"
            );
}
