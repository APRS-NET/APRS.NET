namespace AprsNet.Packets.Tests;

public class AprsDevicesTests
{
    private static readonly string _deviceIdDirectory = Path.Combine(RepositoryRoot(), "tools", "deviceid");

    [Theory]
    [InlineData("APDW17", "DireWolf")] // APDW?? has more literal characters than APDnnn
    [InlineData("APDW17-1", "DireWolf")] // the SSID is ignored
    [InlineData("APD123", "aprsd")] // only APDnnn matches
    [InlineData("APDST1", "dsTracker")] // APDST? beats APDS??
    [InlineData("APDS12", "dsDIGI")]
    [InlineData("APBT62", "DMR 6x2")] // an exact TOCALL beats APBT*
    [InlineData("APBTXX", null)] // APBT*, which names a vendor only
    [InlineData("APK004", "TH-D74")]
    [InlineData("APK012", "TH-D7")] // APK0??
    [InlineData("APZNET", "Experimental")] // APZ*
    [InlineData("APRS", "Unknown")]
    public void FindsTheMostSpecificTocall(string destination, string? model)
    {
        var device = AprsDevices.FromTocall(destination);

        Assert.NotNull(device);
        Assert.Equal(model, device.Model);
    }

    [Theory]
    [InlineData("N0CALL")]
    [InlineData("")]
    [InlineData("APDWXYZ")] // longer than any pattern of that length
    [InlineData("APDW1")] // shorter
    public void ReturnsNullForUnknownDestinations(string destination) =>
        Assert.Null(AprsDevices.FromTocall(destination));

    [Fact]
    public void DescribesTheDevice()
    {
        var direwolf = AprsDevices.FromTocall("APDW17")!;
        var aprsd = AprsDevices.FromTocall("APD123")!;
        var kenwood = AprsDevices.FromTocall("APK004")!;

        Assert.Equal("WB2OSZ DireWolf", direwolf.ToString());
        Assert.Null(direwolf.Class); // the list gives no class for it
        Assert.Equal(AprsDeviceClass.Software, aprsd.Class);
        Assert.Equal("Linux/Unix", aprsd.OperatingSystem);
        Assert.Equal("BTECH", AprsDevices.FromTocall("APBTXX")!.ToString());
        Assert.Equal(AprsDeviceClass.HandHeld, kenwood.Class);
        Assert.Equal(AprsDeviceFeatures.None, kenwood.Features);

        // The list gives the messaging feature to legacy Mic-E radios, which cannot say so in their packets.
        var legacy = AprsDevices.Identify(AprsPacket.Parse("N0CALL-9>S32UVT:`(_fn\"Oj/>Hello^"))!;
        Assert.Equal("Kenwood TH-D74", legacy.ToString());
        Assert.Equal(AprsDeviceFeatures.Messaging, legacy.Features);
    }

    [Fact]
    public void IdentifiesPacketsByDestinationOrMicECode()
    {
        Assert.Equal("DireWolf", AprsDevices.Identify(AprsPacket.Parse("N0CALL>APDW17,WIDE1-1:>hi"))?.Model);

        // Mic-E: the destination is the latitude; the device comes from the comment's codes.
        var micE = AprsPacket.Parse("N0CALL-9>S32UVT:`(_fn\"Oj/`Hello _%");
        Assert.Equal("FTM-400DR", AprsDevices.Identify(micE)?.Model);
        Assert.Same(((MicEReport)micE.Payload!).Device, AprsDevices.Identify(micE));
        Assert.Null(AprsDevices.Identify(AprsPacket.Parse("N0CALL-9>S32UVT:`(_fn\"Oj/`Hello")));

        Assert.Null(AprsDevices.Identify(AprsPacket.Parse("no header")));
        Assert.Throws<ArgumentNullException>(() => AprsDevices.Identify(null!));
        Assert.Throws<ArgumentNullException>(() => AprsDevices.FromTocall(null!));
    }

    [Fact]
    public void RecordsTheListVersion()
    {
        var version = File.ReadAllText(Path.Combine(_deviceIdDirectory, "tocalls.version")).Trim().Split(' ');

        Assert.Equal(version[0], AprsDevices.ListCommit);
        Assert.Equal(DateOnly.ParseExact(version[1], "yyyy-MM-dd"), AprsDevices.ListDate);
    }

    [Fact]
    public void EveryTocallInThePinnedListResolves()
    {
        // The generator's output against its input: every exact TOCALL finds its own model, and every pattern finds a
        // device when filled in (perhaps a more specific one).
        var entries = File.ReadLines(Path.Combine(_deviceIdDirectory, "tocalls.yaml"))
            .SkipWhile(line => line != "tocalls:")
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- tocall: ", StringComparison.Ordinal))
            .Select(line => line["- tocall: ".Length..].Trim('"'))
            .ToList();

        Assert.True(entries.Count > 400, $"only {entries.Count} TOCALLs read");
        foreach (var tocall in entries)
        {
            var filled = tocall.Replace('n', '0').Replace('?', 'Q').Replace("*", "", StringComparison.Ordinal);
            Assert.True(AprsDevices.FromTocall(filled) is not null, $"{tocall} ({filled}) did not resolve");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AprsNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("AprsNet.slnx not found above the tests.");
    }
}
