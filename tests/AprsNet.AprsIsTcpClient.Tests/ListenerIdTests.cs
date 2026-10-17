using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>Listen-only logins with a generated listener ID, for someone without a callsign.</summary>
public sealed class ListenerIdTests
{
    private static AprsIsClientOptions ListenOnly(Action<AprsIsClientOptions>? configure = null)
    {
        var options = new AprsIsClientOptions
        {
            ListenOnly = true,
            SoftwareName = "test",
            SoftwareVersion = "1.0",
        };
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public void AListenerIdIsSwlAndSixDigits()
    {
        var id = AprsIsLogin.NewListenerId(new Random(42));

        Assert.Matches("^SWL[0-9]{6}$", id);
        Assert.Equal(id, AprsIsLogin.NewListenerId(new Random(42)));
        Assert.NotEqual(id, AprsIsLogin.NewListenerId(new Random(43)));
        Assert.True(AprsIsLogin.IsListenerId(id));
    }

    [Theory]
    [InlineData("SWL482913", true)]
    [InlineData("swl000000", true)]
    [InlineData("SWL48291", false)]
    [InlineData("SWL4829130", false)]
    [InlineData("SWL48291A", false)]
    [InlineData("NZ0P", false)]
    [InlineData("N0CALL-10", false)]
    [InlineData("SW1ABC", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ListenerIdsAreToldFromCallsigns(string? login, bool isListener) =>
        Assert.Equal(isListener, AprsIsLogin.IsListenerId(login));

    [Fact]
    public void AListenerIdIsAValidLoginButNeverAnAx25Source()
    {
        var options = ListenOnly(o => o.Callsign = "SWL482913");
        options.Validate();

        Assert.Equal("SWL482913", options.Callsign);
        Assert.True(options.IsReadOnly);
        Assert.False(Callsign.Parse("SWL482913").IsAx25); // too long for an AX.25 address
    }

    [Fact]
    public void ValidateFillsInAListenerIdWhenThereIsNoCallsign()
    {
        var options = ListenOnly();
        options.Validate();

        Assert.True(AprsIsLogin.IsListenerId(options.Callsign));
    }

    [Fact]
    public async Task AListenOnlyLoginUsesPasscodeMinusOneWhateverItIsGiven()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp SWL482913 unverified, server T2TEST");
        await using var client = new AprsIsClient(
            ListenOnly(o =>
            {
                o.Callsign = "SWL482913";
                o.Passcode = 12345;
            }),
            transport: transport
        );

        var result = await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LoginStatus.Unverified, result.Status);
        Assert.Equal(["user SWL482913 pass -1 vers test 1.0"], transport.Written);
    }

    [Fact]
    public async Task AListenOnlyClientRefusesToSendAndSaysWhy()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp SWL482913 unverified, server T2TEST");
        await using var client = new AprsIsClient(ListenOnly(o => o.Callsign = "SWL482913"), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAsync(AprsPacket.Parse("NZ0P>APZNET:>hello"), TestContext.Current.CancellationToken)
        );

        Assert.Contains("listen-only (SWL482913)", ex.Message, StringComparison.Ordinal);
        Assert.Single(transport.Written); // just the login
    }

    [Fact]
    public async Task AListenOnlyConnectionRefusesAtOnceWithoutWaitingToConnect()
    {
        await using var connection = new AprsIsConnection(ListenOnly(o => o.Callsign = "SWL482913"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await connection.SendAsync(AprsPacket.Parse("NZ0P>APZNET:>hello"), TestContext.Current.CancellationToken)
        );

        Assert.Contains("listen-only", ex.Message, StringComparison.Ordinal);
    }
}
