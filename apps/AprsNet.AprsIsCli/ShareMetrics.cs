using System.Diagnostics.Metrics;

namespace AprsNet.AprsIsCli;

/// <summary>
/// Instruments for <c>tnc share</c>, on the <c>AprsNet.Cli</c> meter, which the OTLP exporter picks up with the
/// libraries' <c>AprsNet.*</c> meters. The apps and frames themselves are counted by <c>KissTcpServer</c>.
/// </summary>
internal static class ShareMetrics
{
    public const string MeterName = "AprsNet.Cli";

    private static readonly Meter _meter = new(MeterName);

    private static readonly Histogram<double> _wait = _meter.CreateHistogram<double>(
        "aprscli.tnc_share.frame.wait",
        "s",
        "How long each frame from an app waited for the TNC to be free before it was written to the TNC.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0, 0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60] }
    );

    public static void Waited(TimeSpan wait, string tnc) =>
        _wait.Record(wait.TotalSeconds, new KeyValuePair<string, object?>("tnc", tnc));
}
