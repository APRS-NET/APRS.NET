namespace AprsNet.Corpus.Tests;

public sealed class CorpusFileTests : IDisposable
{
    private static readonly string _corpus = Path.Combine(AppContext.BaseDirectory, "corpus");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aprsnet-corpus-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        foreach (
            var path in Directory.GetFiles(_corpus, "*.log", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        )
        {
            data.Add(Path.GetRelativePath(_corpus, path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void EveryCorpusFileReadsAndWritesBackByteForByte(string file)
    {
        var original = File.ReadAllBytes(Path.Combine(_corpus, file));
        using var stream = new MemoryStream();
        using (var writer = new CorpusWriter(stream, leaveOpen: true))
        {
            foreach (var line in CorpusFile.ReadLines(Path.Combine(_corpus, file)))
            {
                writer.Write(line);
            }
        }

        Assert.Equal(original, stream.ToArray());
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void AnonymizingRealTrafficKeepsEveryLineItsLengthTimeAndDestination(string file)
    {
        var lines = CorpusFile.ReadLines(Path.Combine(_corpus, file)).ToList();

        var result = new CorpusAnonymizer().Anonymize(lines);

        Assert.Empty(result.LeftOut);
        Assert.Equal(lines.Count, result.Lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal(lines[i].ReceivedAt, result.Lines[i].ReceivedAt);
            Assert.Equal(lines[i].Packet.Length, result.Lines[i].Packet.Length);
            Assert.Equal(Destination(lines[i].Packet), Destination(result.Lines[i].Packet)); // TOCALL or Mic-E data
        }

        // The corpus is anonymized already, so its pseudonyms look like callsigns and get new ones; the check still runs.
        Assert.NotNull(result.Leaks);
    }

    private static string Destination(string packet)
    {
        var gt = packet.IndexOf('>', StringComparison.Ordinal);
        var end = packet.IndexOfAny([',', ':'], gt + 1);
        return gt < 0 || end < 0 ? "" : packet[(gt + 1)..end];
    }

    [Fact]
    public void EmptyLinesAreSkippedAndABadLineIsNamedByItsNumber()
    {
        var path = Write(
            "2026-10-12T03:21:18.012Z N0CALL>APRS:>one\n\n2026-10-12T03:21:19.000Z N0CALL>APRS:>two\nnot a line\n"
        );

        var ex = Assert.Throws<FormatException>(() => CorpusFile.ReadLines(path).ToList());

        Assert.Contains("Line 4", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, CorpusFile.ReadLines(path).Take(2).Count());
    }

    [Fact]
    public async Task LinesCanBeReadAsTheyArrive()
    {
        var path = Write("2026-10-12T03:21:18.012Z N0CALL>APRS:>one\n2026-10-12T03:21:19.000Z N0CALL>APRS:>two\n");
        await using var stream = File.OpenRead(path);

        var packets = new List<string>();
        await foreach (
            var line in CorpusFile.ReadLinesAsync(stream, cancellationToken: TestContext.Current.CancellationToken)
        )
        {
            packets.Add(line.Packet);
        }

        Assert.Equal(["N0CALL>APRS:>one", "N0CALL>APRS:>two"], packets);
    }

    [Fact]
    public async Task AppendingCreatesTheFolderAndAddsToTheEnd()
    {
        var path = Path.Combine(_directory, "capture", "unparsed.log");
        var first = new CorpusLine(new DateTimeOffset(2026, 10, 12, 3, 21, 18, TimeSpan.Zero), "N0CALL>APRS:>one");
        var second = new CorpusLine(first.ReceivedAt.AddSeconds(1), "N0CALL>APRS:>two");

        await using (var writer = CorpusFile.OpenAppend(path))
        {
            await writer.WriteAsync(first, TestContext.Current.CancellationToken);
            Assert.Equal([first], CorpusFile.ReadLines(path)); // flushed at once, readable meanwhile
        }

        await using (var writer = CorpusFile.OpenAppend(path))
        {
            await writer.WriteAsync(second, TestContext.Current.CancellationToken);
        }

        Assert.Equal([first, second], CorpusFile.ReadLines(path));
    }

    [Fact]
    public void WriteLinesReplacesUnlessAskedToAppend()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "out.log");
        var line = new CorpusLine(new DateTimeOffset(2026, 10, 12, 3, 21, 18, TimeSpan.Zero), "N0CALL>APRS:>one");

        CorpusFile.WriteLines(path, [line, line]);
        CorpusFile.WriteLines(path, [line]);
        Assert.Single(CorpusFile.ReadLines(path));

        CorpusFile.WriteLines(path, [line], append: true);
        Assert.Equal(2, CorpusFile.ReadLines(path).Count());
    }

    [Fact]
    public void ADefaultLineCannotBeWritten()
    {
        using var writer = new CorpusWriter(new MemoryStream());

        Assert.Throws<ArgumentException>(() => writer.Write(default));
    }

    private string Write(string text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.log");
        File.WriteAllText(path, text, CorpusFile.Encoding);
        return path;
    }
}
