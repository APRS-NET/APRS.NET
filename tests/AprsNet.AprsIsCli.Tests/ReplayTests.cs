using System.Text;
using System.Text.Json;

namespace AprsNet.AprsIsCli.Tests;

public sealed class ReplayTests : IDisposable
{
    private const string _position = "N0CALL-9>APRS,WIDE1-1:=4903.50N/07201.75W>088/036 Hi";
    private const string _badLatitude = "N0CALL>APRS,TCPIP*:!9903.50N/07201.75W-";
    private const string _capabilities = "N0CALL>APRS,TCPIP*:<IGATE,MSG_CNT=0,LOC_CNT=1";
    private const string _status = "N0CALL>APRS,TCPIP*:>status text";

    private readonly string _directory = Directory.CreateTempSubdirectory("aprscli-replay-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string WriteFile(string name, params string[] lines)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", Encoding.Latin1);
        return path;
    }

    private static Task<CliResult> RunAsync(CliHarness harness, params string[] args) =>
        harness.RunAsync(["replay", .. args], TestContext.Current.CancellationToken);

    [Fact]
    public async Task CountsOutcomesAndSkipsServerCommentsAndBlankLines()
    {
        var file = WriteFile(
            "mixed.log",
            "# aprsc 2.1.19",
            "2026-10-14T05:34:02.728Z " + _position,
            "",
            _badLatitude,
            _capabilities
        );

        var result = await RunAsync(new CliHarness(), file);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.StartsWith(
            "3 packets from 5 lines in 1 input (1 server comment and 1 blank line skipped)",
            result.Out,
            StringComparison.Ordinal
        );
        Assert.Matches(@"Parsed\s+1\s+33\.3%", result.Out);
        Assert.Matches(@"Unsupported\s+1\s+33\.3%", result.Out);
        Assert.Matches(@"Malformed\s+1\s+33\.3%", result.Out);
        Assert.Matches(@"Position\s+2\s+1\s+0\s+1", result.Out);
    }

    [Fact]
    public async Task ListsIssuesWithTheirDescriptionAndExampleLocations()
    {
        var file = WriteFile("issues.log", _status, _badLatitude, _badLatitude);

        var result = await RunAsync(new CliHarness(), file);

        Assert.Matches(@"InvalidLatitude\s+error\s+2  The latitude is malformed or out of range\.", result.Out);
        Assert.Contains($"{file}:2  {_badLatitude}", result.Out, StringComparison.Ordinal);
        Assert.Contains($"{file}:3  {_badLatitude}", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupsUndecodedPacketsByDataTypeIdentifier()
    {
        var file = WriteFile(
            "undecoded.log",
            _capabilities,
            _capabilities,
            "N0CALL>APRS:{{ABCDEF",
            "N0CALL>APRS:\x01?"
        );

        var result = await RunAsync(new CliHarness(), file);

        Assert.Matches(@"'<'\s+2", result.Out);
        Assert.Matches(@"'\{'\s+1", result.Out);
        Assert.Matches(@"0x01\s+1", result.Out); // unprintable identifiers are shown in hex
        Assert.Contains(@"\x01?", result.Out, StringComparison.Ordinal); // and escaped in examples
    }

    [Fact]
    public async Task LimitsOrHidesExamples()
    {
        var file = WriteFile("many.log", _badLatitude, _badLatitude, _badLatitude, _badLatitude);

        var one = await RunAsync(new CliHarness(), file, "--examples", "1");
        var none = await RunAsync(new CliHarness(), file, "-e", "0");

        Assert.Single(one.Out.Split('\n'), l => l.Contains(_badLatitude, StringComparison.Ordinal));
        Assert.DoesNotContain(_badLatitude, none.Out, StringComparison.Ordinal);
        Assert.Contains("InvalidLatitude", none.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CombinesSeveralFilesAndStandardInput()
    {
        var first = WriteFile("first.log", _position);
        var second = WriteFile("second.log", _status, _status);
        var harness = new CliHarness { Input = _badLatitude + "\n" };

        var result = await RunAsync(harness, first, "-", second);

        Assert.StartsWith("4 packets from 4 lines in 3 inputs", result.Out, StringComparison.Ordinal);
        Assert.Contains($"stdin:1  {_badLatitude}", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsEveryByteAsLatin1()
    {
        // UTF-8 "°C" and a Mic-E style control byte must reach the parser unchanged.
        var path = Path.Combine(_directory, "bytes.log");
        File.WriteAllBytes(path, [.. Encoding.Latin1.GetBytes("N0CALL>APRS:>21"), 0xC2, 0xB0, (byte)'C', (byte)'\n']);

        var result = await RunAsync(new CliHarness(), path, "--format", "json");

        using var json = JsonDocument.Parse(result.Out);
        Assert.Equal(1, json.RootElement.GetProperty("outcomes").GetProperty("parsed").GetInt32());
    }

    [Fact]
    public async Task WritesAJsonSummary()
    {
        var file = WriteFile("summary.log", "# keepalive", _position, _badLatitude, _capabilities);

        var result = await RunAsync(new CliHarness(), file, "--format", "json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Out);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("inputs").GetInt32());
        Assert.Equal(4, root.GetProperty("lines").GetInt32());
        Assert.Equal(1, root.GetProperty("serverComments").GetInt32());
        Assert.Equal(3, root.GetProperty("packets").GetInt32());
        Assert.Equal(1, root.GetProperty("outcomes").GetProperty("malformed").GetInt32());
        Assert.Equal(1, root.GetProperty("types").GetProperty("position").GetProperty("parsed").GetInt32());

        var issue = root.GetProperty("issues")[0];
        Assert.Equal("InvalidLatitude", issue.GetProperty("code").GetString());
        Assert.Equal("error", issue.GetProperty("severity").GetString());
        Assert.Equal(1, issue.GetProperty("packets").GetInt32());
        Assert.Equal($"{file}:3", issue.GetProperty("examples")[0].GetProperty("location").GetString());

        var undecoded = root.GetProperty("undecoded")[0];
        Assert.Equal("'<'", undecoded.GetProperty("identifier").GetString());
        Assert.Equal(_capabilities, undecoded.GetProperty("examples")[0].GetProperty("line").GetString());
    }

    [Fact]
    public async Task RejectsAMissingFileBeforeReadingAnything()
    {
        var present = WriteFile("present.log", _position);

        var result = await RunAsync(new CliHarness(), present, Path.Combine(_directory, "missing.log"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Contains("no such file", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--examples", "-1")]
    [InlineData("-", "-")]
    public async Task RejectsBadOptions(params string[] args)
    {
        var result = await RunAsync(new CliHarness { Input = "" }, args);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task RequiresAFile()
    {
        var result = await RunAsync(new CliHarness());

        Assert.Equal(3, result.ExitCode);
    }
}
