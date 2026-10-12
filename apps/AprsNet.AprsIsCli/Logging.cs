using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace AprsNet.AprsIsCli;

/// <summary>
/// Console logging plus, when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, OTLP export of logs and metrics.
/// Endpoint, headers and protocol come from the standard OTEL_* environment variables.
/// </summary>
internal static class Logging
{
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Logger category for received packets. Exported to OpenTelemetry only, never written to the console.</summary>
    public const string PacketCategory = "aprscli.packets";

    public static bool IsOtlpConfigured(Func<string, string?> getEnvironmentVariable) =>
        !string.IsNullOrWhiteSpace(getEnvironmentVariable(OtlpEndpointVariable));

    public static ILoggerFactory CreateFactory(LogLevel minimumLevel = LogLevel.Information)
    {
        var otlpEnabled = IsOtlpConfigured(Environment.GetEnvironmentVariable);

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.UseUtcTimestamp = true;
            });

            // Packets own stdout, so every log line goes to stderr.
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            // Per-packet logs would drown the terminal; they go to OpenTelemetry only.
            builder.AddFilter<ConsoleLoggerProvider>(PacketCategory, LogLevel.None);

            if (otlpEnabled)
            {
                builder.AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(Resource());
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.AddOtlpExporter();
                });
            }
        });
    }

    /// <summary>
    /// Exports the client's <c>AprsNet.*</c> meters over OTLP, or returns null when no endpoint is configured.
    /// Dispose it before exit so the final readings are flushed.
    /// </summary>
    public static MeterProvider? CreateMeterProvider(Func<string, string?> getEnvironmentVariable)
    {
        if (!IsOtlpConfigured(getEnvironmentVariable))
        {
            return null;
        }

        return Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(Resource())
            .AddMeter("AprsNet.*")
            .AddOtlpExporter()
            .Build();
    }

    private static ResourceBuilder Resource() =>
        ResourceBuilder
            .CreateDefault()
            .AddService("aprscli", serviceVersion: typeof(Logging).Assembly.GetName().Version?.ToString());
}
