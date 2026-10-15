using System.Diagnostics.Metrics;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>
/// Checks the counters other tests do not: messages to the login callsign, active connections and bytes sent. Other
/// tests run at the same time and record too, so each check looks for this test's own measurements.
/// </summary>
public sealed class ClientMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, string? Kind)> _measurements = [];

    public ClientMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "AprsNet.AprsIsTcpClient")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
            {
                string? kind = null;
                foreach (var tag in tags)
                {
                    kind = tag.Key == "kind" ? (string?)tag.Value : kind;
                }

                lock (_measurements)
                {
                    _measurements.Add((instrument.Name, value, kind));
                }
            }
        );
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    private List<(string Instrument, long Value, string? Kind)> Measurements(string instrument)
    {
        lock (_measurements)
        {
            return [.. _measurements.Where(m => m.Instrument == instrument)];
        }
    }

    private static AprsIsClientOptions Options() =>
        new()
        {
            Callsign = "NZ0P-7",
            SoftwareName = "test",
            SoftwareVersion = "1.0",
        };

    [Fact]
    public async Task CountsMessagesAddressedToTheLoginCallsign()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P-7 unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        transport.Server("WU2Z>APRS::nz0p-7   :Hello{1"); // any case
        transport.Server("WU2Z>APRS::NZ0P-7   :ack9");
        transport.Server("WU2Z>APRS::NZ0P     :Other SSID{2");
        transport.Server("WU2Z>APRS::BLN1     :Bulletin");

        var read = 0;
        await foreach (var _ in client.ReadPacketsAsync(TestContext.Current.CancellationToken))
        {
            if (++read == 4)
            {
                break;
            }
        }

        var kinds = Measurements("aprsnet.aprsis.packets.addressed_to_user").Select(m => m.Kind).ToList();
        Assert.Contains("message", kinds);
        Assert.Contains("ack", kinds);
    }

    [Fact]
    public async Task CountsAConnectionAsActiveUntilItEnds()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P-7 unverified, server T2TEST");
        var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Measurements("aprsnet.aprsis.connection.active"), m => m.Value == 1);

        transport.Close();
        await client.DisposeAsync();

        Assert.Contains(Measurements("aprsnet.aprsis.connection.active"), m => m.Value == -1);
    }

    [Fact]
    public async Task CountsTheBytesOfEveryLineSent()
    {
        var transport = new FakeAprsTransport();
        transport.Server("# logresp NZ0P-7 unverified, server T2TEST");
        await using var client = new AprsIsClient(Options(), transport: transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await client.SetFilterAsync("r/35/-97/200", TestContext.Current.CancellationToken);

        var expected = transport.Written.Sum(line => line.Length + 2);
        Assert.True(
            Measurements("aprsnet.aprsis.bytes.sent").Sum(m => m.Value) >= expected,
            "the login and filter lines should be counted, CR LF included"
        );
    }
}
