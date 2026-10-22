using System.Reflection;
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

    /// <summary>
    /// Logger category for what <c>serve</c> and <c>tnc share</c> report on stderr, such as an app connecting or the
    /// TNC lost, as structured logs. Exported to OpenTelemetry only, since the same lines are already on stderr.
    /// </summary>
    public const string EventCategory = "aprscli.events";

    /// <summary>
    /// The <c>service.instance.id</c> to report when <c>OTEL_RESOURCE_ATTRIBUTES</c> doesn't give one, such as the
    /// instance of a systemd template service. Without either, each run gets a new one.
    /// </summary>
    public const string InstanceVariable = "APRSNET_INSTANCE";

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
            builder.AddFilter<ConsoleLoggerProvider>(EventCategory, LogLevel.None);

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

    /// <summary>
    /// The resource every log and metric carries: <c>service.name</c> <c>aprscli</c>, its version and an instance ID
    /// (from <c>APRSNET_INSTANCE</c>, or new for each run). The standard <c>OTEL_SERVICE_NAME</c> and
    /// <c>OTEL_RESOURCE_ATTRIBUTES</c> are read last, so they win.
    /// </summary>
    internal static ResourceBuilder Resource(Func<string, string?> getEnvironmentVariable)
    {
        var instance = getEnvironmentVariable(InstanceVariable) is { Length: > 0 } given ? given : null;
        return ResourceBuilder
            .CreateEmpty()
            .AddService(
                "aprscli",
                serviceVersion: Version,
                autoGenerateServiceInstanceId: instance is null,
                serviceInstanceId: instance
            )
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();
    }

    private static ResourceBuilder Resource() => Resource(Environment.GetEnvironmentVariable);

    /// <summary>The release, such as <c>0.6.0-alpha.2</c>: the informational version without its commit.</summary>
    private static string? Version =>
        typeof(Logging)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0];
}
