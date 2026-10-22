using System.Diagnostics.Metrics;
using AprsNet.Ax25;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli.Tests;

/// <summary>What aprscli tells OpenTelemetry about itself, and the meters of its own.</summary>
public sealed class TelemetryTests
{
    private static Dictionary<string, object> Attributes(Dictionary<string, string> environment) =>
        Logging
            .Resource(name => environment.GetValueOrDefault(name))
            .Build()
            .Attributes.ToDictionary(a => a.Key, a => a.Value);

    [Fact]
    public void AServiceInstanceNamesItself()
    {
        var attributes = Attributes(new() { ["APRSNET_INSTANCE"] = "tncx" });

        Assert.Equal("tncx", attributes["service.instance.id"]);
        Assert.Equal("aprscli", attributes["service.name"]); // the test runner sets no OTEL_SERVICE_NAME
        Assert.DoesNotContain("+", (string)attributes["service.version"], StringComparison.Ordinal);
        Assert.NotEqual("0.0.0.0", attributes["service.version"]);
    }

    [Fact]
    public void WithoutAnInstanceTheRunGetsOne()
    {
        var attributes = Attributes([]);

        Assert.True(Guid.TryParse((string)attributes["service.instance.id"], out _));
    }

    [Fact]
    public async Task ThePacerRecordsHowLongEachFrameWaited()
    {
        var tnc = "t" + Guid.NewGuid().ToString("N")[..8];
        var waits = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ShareMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "tnc" && (string?)tag.Value == tnc)
                    {
                        lock (waits)
                        {
                            waits.Add(value);
                        }
                    }
                }
            }
        );
        listener.Start();
        var pacer = new FramePacer(TimeProvider.System, 1200, serialBaud: null, TimeSpan.Zero, tnc);
        var frame = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL>APRS:>x"));

        await pacer.SendAsync(frame, (_, _) => ValueTask.CompletedTask, TestContext.Current.CancellationToken);
        await pacer.SendAsync(frame, (_, _) => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(2, waits.Count);
        Assert.True(waits[0] < 0.05, $"the first frame waited {waits[0]}s");
        Assert.True(waits[1] >= 0.1, $"the second frame waited {waits[1]}s for the first to be sent"); // ~0.13s on air
    }
}
