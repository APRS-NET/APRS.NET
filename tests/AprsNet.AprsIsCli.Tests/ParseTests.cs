using System.Text;
using System.Text.Json;

namespace AprsNet.AprsIsCli.Tests;

public sealed class ParseTests : IDisposable
{
    private const string _position = "N0CALL-9>APRS,WIDE1-1:=4903.50N/07201.75W>088/036 Hi";
    private const string _status = "N0CALL>APRS,TCPIP*:>status text";

    private readonly string _directory = Directory.CreateTempSubdirectory("aprscli-parse-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static Task<CliResult> RunAsync(CliHarness harness, params string[] args) =>
        harness.RunAsync(["parse", .. args], TestContext.Current.CancellationToken);

    private static string[] OutputLines(CliResult result) =>
        result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task PrintsAPrettyBlockForAnArgument()
    {
        var result = await RunAsync(new CliHarness(), _position);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Contains("N0CALL-9 → APRS", result.Out, StringComparison.Ordinal);
        Assert.Contains("49.05833  -72.02917", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", result.Out, StringComparison.Ordinal); // no colour off a terminal
    }

    [Fact]
    public async Task PrintsOneDecodedLinePerArgument()
    {
        var result = await RunAsync(new CliHarness(), "--format", "decoded", _position, _status);

        var lines = OutputLines(result);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("N0CALL-9>APRS,WIDE1-1  position", lines[0], StringComparison.Ordinal);
        Assert.Equal("N0CALL>APRS,TCPIP*  status  \"status text\"", lines[1]);
    }

    [Fact]
    public async Task PrintsWhatARawNmeaSentenceSays()
    {
        var result = await RunAsync(
            new CliHarness(),
            "--format",
            "decoded",
            "N0CALL>APRS:$GPRMC,063909,A,3349.4302,N,11700.3721,W,43.022,89.3,291099,13.6,E*52",
            "N0CALL>APRS:$GPGGA,102705,5157.9762,N,00029.3256,W,1,04,2.0,75.7,M,47.6,M,,*62",
            "N0CALL>APRS:$GPRMC,063909,V,,,,,,,291099,,*3E"
        );

        Assert.Equal(
            [
                "N0CALL>APRS  NMEA GPRMC  fix  33.82384 -117.00620  089° 43 kn  at 1999-10-29 06:39:09 UTC",
                "N0CALL>APRS  NMEA GPGGA  fix  51.96627 -0.48876  75.7 m  at 10:27:05 UTC",
                "N0CALL>APRS  NMEA GPRMC  no fix  at 1999-10-29 06:39:09 UTC",
            ],
            OutputLines(result)
        );
    }

    [Fact]
    public async Task PrintsCapabilitiesUserDefinedDataAndRawWeather()
    {
        var result = await RunAsync(
            new CliHarness(),
            "--format",
            "decoded",
            "N0CALL>APRS:<IGATE,MSG_CNT=0,LOC_CNT=1",
            "N0CALL>APRS:{Q1qwerty",
            "N0CALL>APRS:{{zasdfg",
            "N0CALL>APRS:!!006B005803500000----03E9--------002105140000005D"
        );

        Assert.Equal(
            [
                "N0CALL>APRS  capabilities  IGATE, MSG_CNT=0, LOC_CNT=1",
                "N0CALL>APRS  user-defined  user Q, type 1  \"qwerty\"",
                "N0CALL>APRS  user-defined  experimental, type z  \"asdfg\"",
                "N0CALL>APRS  raw weather  Ultimeter 2000 (data logging)  006B005803500000----03E9--------002105140000005D",
            ],
            OutputLines(result)
        );
    }

    [Fact]
    public async Task WritesCapabilitiesAsJsonInOrder()
    {
        var result = await RunAsync(new CliHarness(), "--format", "json", "N0CALL>APRS:<IGATE,MSG_CNT=3");

        using var json = JsonDocument.Parse(OutputLines(result)[0]);
        var capabilities = json.RootElement.GetProperty("capabilities");
        Assert.Equal("IGATE", capabilities[0].GetProperty("token").GetString());
        Assert.False(capabilities[0].TryGetProperty("value", out _));
        Assert.Equal("3", capabilities[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task ParsesArgumentsAsUtf8Text()
    {
        var result = await RunAsync(new CliHarness(), "--format", "decoded", "N0CALL>APRS:>状态 21°C");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("N0CALL>APRS  status  \"状态 21°C\"", OutputLines(result)[0]);
    }

    [Fact]
    public async Task ReadsAFileSkippingServerCommentsAndBlankLines()
    {
        var path = Path.Combine(_directory, "packets.log");
        await File.WriteAllTextAsync(
            path,
            $"# aprsc 2.1.19\n{_position}\n\n{_status}\n",
            Encoding.Latin1,
            TestContext.Current.CancellationToken
        );

        var result = await RunAsync(new CliHarness(), "-i", path, "--format", "decoded");

        Assert.Equal(2, OutputLines(result).Length);
    }

    [Fact]
    public async Task ReadsStandardInputAndUsesCaptureTimestamps()
    {
        var harness = new CliHarness { Input = $"2026-10-14T05:34:02.728Z {_status}\n" };

        var result = await RunAsync(harness, "--format", "json");

        using var json = JsonDocument.Parse(OutputLines(result)[0]);
        Assert.Equal(_status, json.RootElement.GetProperty("raw").GetString());
        Assert.StartsWith(
            "2026-10-14T05:34:02.728",
            json.RootElement.GetProperty("received").GetString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PrintsMalformedPacketsWithTheirIssues()
    {
        var result = await RunAsync(new CliHarness(), "--format", "json", "N0CALL>APRS,TCPIP*:!9903.50N/07201.75W-");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(OutputLines(result)[0]);
        Assert.Equal("malformed", json.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task UsesColourWhenAsked()
    {
        var result = await RunAsync(new CliHarness(), "--color", "always", _position);

        Assert.Contains("\u001b[", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesToWaitOnATerminal()
    {
        var result = await RunAsync(new CliHarness { InIsTerminal = true });

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("give a packet", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPacketsTogetherWithAFile()
    {
        var path = Path.Combine(_directory, "packets.log");
        await File.WriteAllTextAsync(path, _status, TestContext.Current.CancellationToken);

        var result = await RunAsync(new CliHarness(), "-i", path, _position);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task RejectsAMissingFile()
    {
        var result = await RunAsync(new CliHarness(), "--file", Path.Combine(_directory, "missing.log"));

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("no such file", result.Error, StringComparison.Ordinal);
    }
}
