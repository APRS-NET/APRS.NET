using AprsNet.Packets;

namespace AprsNet.Kiss.Tests;

/// <summary>
/// Runs against a real KISS TNC when <c>APRSNET_KISS_TNC</c> is set to its <c>host:port</c>, and is skipped
/// otherwise. The TNC needs Dire Wolf's <c>KISSCOPY 1</c>, which passes a frame sent by one KISS client to the
/// others; with <c>ADEVICE null null</c> nothing reaches a radio:
/// <code>
/// ADEVICE null null
/// MYCALL N0CALL
/// KISSPORT 8011
/// KISSCOPY 1
/// </code>
/// </summary>
public class RealTncTests
{
    private static (string Host, int Port)? Tnc() =>
        Environment.GetEnvironmentVariable("APRSNET_KISS_TNC")?.Split(':') is [var host, var port]
            ? (host, int.Parse(port, System.Globalization.CultureInfo.InvariantCulture))
            : null;

    [Fact]
    public async Task AFrameSentByOneClientReachesAnotherThroughTheTnc()
    {
        Assert.SkipWhen(Tnc() is null, "Set APRSNET_KISS_TNC=host:port to run against a real TNC with KISSCOPY.");
        var (host, port) = Tnc()!.Value;
        await using var sender = new KissTcpClient(host, port);
        await using var receiver = new KissTcpClient(host, port);
        await sender.ConnectAsync(TestContext.Current.CancellationToken);
        await receiver.ConnectAsync(TestContext.Current.CancellationToken);
        // Mic-E and a byte above 0x7F make sure every information byte crosses unchanged.
        var packets = new[]
        {
            AprsPacket.Parse("N0CALL-9>APZ123,WIDE1-1:!4903.50N/07201.75W>KISS test °"),
            AprsPacket.Parse("N0CALL-9>S32U6T,WIDE1-1,WIDE2-1:`(_fn\"Oj/]Mic-E"),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var reading = Task.Run(
            async () =>
            {
                var received = new List<AprsPacket>();
                await foreach (var packet in receiver.ReadPacketsAsync(timeout.Token))
                {
                    received.Add(packet);
                    if (received.Count == packets.Length)
                    {
                        break;
                    }
                }

                return received;
            },
            TestContext.Current.CancellationToken
        );
        foreach (var packet in packets)
        {
            await sender.SendAsync(packet, TestContext.Current.CancellationToken);
        }

        var received = await reading;

        Assert.Equal(packets.Select(p => p.Raw.ToArray()), received.Select(p => p.Raw.ToArray()));
        Assert.IsType<MicEReport>(received[1].Payload);
    }
}
