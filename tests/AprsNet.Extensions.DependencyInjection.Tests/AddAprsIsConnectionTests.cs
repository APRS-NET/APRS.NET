using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Extensions.DependencyInjection.Tests;

public sealed class AddAprsIsConnectionTests
{
    private static IConfigurationSection Section(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"AprsIs:{s.Key}", s.Value)))
            .Build()
            .GetSection("AprsIs");

    private static ServiceProvider Provider(IConfiguration configuration, Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection();
        more?.Invoke(services);
        services.AddAprsIsConnection(configuration);
        return services.BuildServiceProvider();
    }

    private static AprsIsClientOptions Options(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<AprsIsClientOptions>>().Value;

    [Fact]
    public async Task SettingsAreBoundFromConfiguration()
    {
        await using var provider = Provider(
            Section(
                ("Server", "euro"),
                ("Callsign", "N0CALL-10"),
                ("Passcode", "13023"),
                ("Filter", "r/48.21/16.37/40 t/m"),
                ("SoftwareName", "MyApp"),
                ("SoftwareVersion", "2.0"),
                ("ReconnectDelay", "00:00:07"),
                ("MaxPacketSilence", "00:10:00"),
                ("OverflowPolicy", "DropOldest"),
                ("UseTls", "true")
            )
        );
        var options = Options(provider);
        Assert.Equal(AprsIsEndpoint.Europe, options.Endpoint);
        Assert.Equal("N0CALL-10", options.Callsign);
        Assert.Equal(13023, options.Passcode);
        Assert.Equal("r/48.21/16.37/40 t/m", options.Filter);
        Assert.Equal("MyApp", options.SoftwareName);
        Assert.Equal("2.0", options.SoftwareVersion);
        Assert.Equal(TimeSpan.FromSeconds(7), options.ReconnectDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), options.MaxPacketSilence);
        Assert.Equal(OverflowPolicy.DropOldest, options.OverflowPolicy);
        Assert.True(options.UseTls);
    }

    [Theory]
    [InlineData("noam", "noam.aprs2.net", 14580)]
    [InlineData("AUNZ", "aunz.aprs2.net", 14580)]
    [InlineData("aprs.example.org", "aprs.example.org", 14580)]
    [InlineData("aprs.example.org:10152", "aprs.example.org", 10152)]
    public async Task ServerIsARegionOrAHost(string server, string host, int port)
    {
        await using var provider = Provider(Section(("Server", server), ("Callsign", "N0CALL")));
        Assert.Equal(new AprsIsEndpoint(host, port), Options(provider).Endpoint);
    }

    [Fact]
    public async Task WithoutAServerTheRotateIsUsed()
    {
        await using var provider = Provider(Section(("Callsign", "N0CALL")));
        Assert.Equal(AprsIsEndpoint.Rotate, Options(provider).Endpoint);
    }

    [Fact]
    public async Task AnUnreadableServerIsReportedByItsKey()
    {
        await using var provider = Provider(Section(("Server", "a:b:c"), ("Callsign", "N0CALL")));
        var error = Assert.Throws<OptionsValidationException>(() => Options(provider));
        Assert.Equal("AprsIs:Server 'a:b:c' is not a region key or host[:port].", error.Message);
    }

    [Theory]
    [InlineData("Callsign", "N0 CALL")]
    [InlineData("Filter", "r/1/2/3\r\nuser x")]
    [InlineData("ChannelCapacity", "0")]
    public async Task InvalidSettingsFailWhenFirstUsed(string key, string value)
    {
        var settings = new Dictionary<string, string> { ["Callsign"] = "N0CALL", [key] = value };
        await using var provider = Provider(Section([.. settings.Select(s => (s.Key, s.Value))]));
        Assert.Throws<OptionsValidationException>(() => Options(provider));
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<AprsIsConnection>());
    }

    [Fact]
    public async Task TheConnectionIsOneSingletonDisposedWithTheContainer()
    {
        var provider = Provider(Section(("Callsign", "N0CALL")));
        var connection = provider.GetRequiredService<AprsIsConnection>();
        Assert.Same(connection, provider.GetRequiredService<AprsIsConnection>());
        Assert.Equal(AprsIsConnectionState.NotStarted, connection.State);

        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task TheContainersClockIsUsed()
    {
        var clock = new FakeTimeProvider();
        await using var provider = Provider(
            Section(("Callsign", "N0CALL")),
            services => services.AddSingleton<TimeProvider>(clock)
        );
        Assert.Same(clock, Options(provider).TimeProvider);
    }

    [Fact]
    public async Task WithoutAClockTheSystemOneIsUsed()
    {
        await using var provider = Provider(Section(("Callsign", "N0CALL")));
        Assert.Same(TimeProvider.System, Options(provider).TimeProvider);
    }

    [Fact]
    public async Task OptionsCanBeSetInCode()
    {
        var services = new ServiceCollection().AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        services.AddAprsIsConnection(options =>
        {
            options.Endpoint = AprsIsEndpoint.Asia;
            options.Callsign = "N0CALL";
        });
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(AprsIsEndpoint.Asia, Options(provider).Endpoint);
        Assert.NotNull(provider.GetRequiredService<AprsIsConnection>());
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddAprsIsConnection((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddAprsIsConnection((Action<AprsIsClientOptions>)null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddAprsIsConnection(new ConfigurationBuilder().Build())
        );
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddAprsIsConnection(_ => { }));
    }
}
