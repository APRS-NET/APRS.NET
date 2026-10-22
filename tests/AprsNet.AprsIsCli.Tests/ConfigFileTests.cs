using System.Globalization;
using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli.Tests;

/// <summary><c>--config</c> and <c>--check</c> on <c>serve</c> and <c>tnc share</c>, as the services run them.</summary>
public sealed class ConfigFileTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aprscli-config-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Delete(recursive: true);

    private string Write(string text)
    {
        var path = Path.Combine(_folder.FullName, $"{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, text);
        return path;
    }

    private static Task<CliResult> RunAsync(params string[] args) => new CliHarness().RunAsync(args, Token);

    [Fact]
    public async Task TheFileGivesTheOptionsAndTablesOnlyGroupThem()
    {
        var path = Write(
            """
            # A TNC-X on a Pi
            [tnc]
            tnc = "serial:/dev/ttyUSB0@1200"   # the TNC
            [apps]
            listen = "0.0.0.0:8001"
            max-clients = 4
            [air]
            gap = "1s"
            quiet = true
            """
        );

        var result = await RunAsync("tnc", "share", "--config", path, "--check");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "aprscli: the settings are good. Would share the TNC at /dev/ttyUSB0 at 1200 baud on 0.0.0.0:8001 with "
                + "up to 4 apps, pacing frames for 1200 bit/s on the air with a 1000 ms gap.",
            result.Error.Trim()
        );
    }

    [Fact]
    public async Task TheCommandLineWinsOverTheFile()
    {
        var path = Write("tnc = \"localhost:8001\"\nlisten = \"0.0.0.0:8001\"\n");

        var result = await RunAsync("tnc", "share", "--config", path, "--check", "-l", "127.0.0.1:9001");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("on 127.0.0.1:9001 with", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServeTakesListsNegativeNumbersAndAnIGate()
    {
        var path = Write(
            """
            [aprs-is]
            callsign = "N0CALL-10"
            filter = "r/35/-97/300"
            [sources]
            tnc = ["localhost:8001", "192.168.1.20:8001"]
            [clients]
            listen = "0.0.0.0:14580"
            http = "127.0.0.1:14590"
            [igate]
            igate = true
            lat = 35.5
            lon = -97.25
            comment = "-from a file @home"
            """
        );
        var harness = new CliHarness();
        harness.Environment["APRS_PASSCODE"] = AprsIsPasscode.Compute("N0CALL").ToString(CultureInfo.InvariantCulture);

        var result = await harness.RunAsync(["serve", "--config", path, "--check"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "aprscli: the settings are good. Would serve APRS-IS at rotate.aprs2.net:14580 as N0CALL-10 with the "
                + "filter \"r/35/-97/300\" and the TNC at localhost:8001 and the TNC at 192.168.1.20:8001 on "
                + "0.0.0.0:14580, with HTTP on 127.0.0.1:14590, and gate what the TNCs hear to APRS-IS as N0CALL-10.",
            result.Error.Trim()
        );
    }

    [Fact]
    public async Task CheckFindsWhatTheCommandWouldRefuse()
    {
        var path = Write("[aprs-is]\ncallsign = \"N0CALL\"\nfilter = \"r/35/-97/300\"\n[igate]\nigate = true\n");

        var result = await RunAsync("serve", "--config", path, "--check");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--igate needs a --tnc to gate from.", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "tnc = \"localhost\"\nlistne = \"0.0.0.0:8001\"\n",
        ":2: 'listne' is not an option of 'share'. See 'aprscli tnc share --help'."
    )]
    [InlineData(
        "tnc = \"localhost\"\n[secret]\npasscode = 12345\n",
        ":3: 'passcode': keep the passcode out of the config file"
    )]
    [InlineData("config = \"other.toml\"\n", ":1: 'config': a config file can't name another one.")]
    [InlineData("tnc = [\"localhost\", \"10.0.0.2\"]\n", ":1: 'tnc' takes one value, not a list.")]
    [InlineData("tnc = \"localhost\"\n[apps]\ntnc = \"10.0.0.2\"\n", ":3: 'tnc' is already set on line 1.")]
    [InlineData("tnc = \"localhost\n", ":1:17: Invalid newline in a string")]
    [InlineData("[[tnc]]\ntnc = \"localhost\"\n", ":1: [[tnc]]: a list of tables isn't a setting; use [tnc].")]
    [InlineData("tnc.address = \"localhost\"\n", ":1: 'tnc': dotted keys aren't settings")]
    [InlineData("tnc = \"localhost\"\nlisten = { address = \"0.0.0.0\" }\n", ":2: 'listen' must be a string, a number")]
    public async Task AFileWithAMistakeIsRefusedWithItsLine(string text, string message)
    {
        var path = Write(text);

        var result = await RunAsync("tnc", "share", "--config", path, "--check");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains($"{path}{message}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingFileIsRefused()
    {
        var path = Path.Combine(_folder.FullName, "missing.toml");

        var result = await RunAsync("serve", "--config", path, "--check");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains($"aprscli: --config: can't read {path}", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gateway.toml", "serve")]
    [InlineData("tncx.toml", "tnc share")]
    public async Task TheServicesExampleConfigsAreGood(string file, string command)
    {
        var path = Path.Combine(RepositoryRoot(), "apps", "services", "config", file);

        var result = await RunAsync([.. command.Split(' '), "--config", path, "--check"]);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.StartsWith("aprscli: the settings are good.", result.Error, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AprsNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository wasn't found.");
    }
}
