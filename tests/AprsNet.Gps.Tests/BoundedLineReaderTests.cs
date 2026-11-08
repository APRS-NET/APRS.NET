using System.Text;

namespace AprsNet.Gps.Tests;

public sealed class BoundedLineReaderTests
{
    private static BoundedLineReader Reader(string text, int maxLineLength = 8) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), maxLineLength, Encoding.UTF8);

    private static async Task<List<string>> ReadAllAsync(BoundedLineReader reader)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    [Fact]
    public async Task LinesEndedByLfOrCrLfAreReadWithoutTheirEnding()
    {
        Assert.Equal(["one", "two", ""], await ReadAllAsync(Reader("one\r\ntwo\n\n")));
    }

    [Fact]
    public async Task ALineAsLongAsTheLimitIsRead()
    {
        Assert.Equal(["12345678", "next"], await ReadAllAsync(Reader("12345678\r\nnext\r\n")));
    }

    [Fact]
    public async Task ALineLongerThanTheLimitIsDroppedAndTheNextIsRead()
    {
        Assert.Equal(["next"], await ReadAllAsync(Reader(new string('x', 100) + "\r\nnext\r\n")));
    }

    [Fact]
    public async Task ALastLineWithoutALineEndingIsStillRead()
    {
        Assert.Equal(["one", "last"], await ReadAllAsync(Reader("one\nlast")));
    }

    [Fact]
    public async Task ALastLineTooLongAndNeverEndedIsDropped()
    {
        Assert.Equal(["one"], await ReadAllAsync(Reader("one\n" + new string('x', 100))));
    }

    [Fact]
    public async Task ACharacterOfSeveralBytesIsReadWhole()
    {
        Assert.Equal(["Québec"], await ReadAllAsync(Reader("Québec\n")));
    }
}
