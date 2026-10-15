using AprsNet.AprsIsTcpClient;
using AprsNet.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// In the DI namespace, like the framework's own Add* methods, so they are found without another using.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers an APRS-IS connection with a service collection.</summary>
public static class AprsIsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="AprsIsConnection"/> and its <see cref="AprsIsClientOptions"/>, bound from
    /// <paramref name="configuration"/>. A <c>Server</c> key picks the server: a region key (<c>noam</c>, <c>euro</c>, …)
    /// or <c>host[:port]</c>. Every other key is an <see cref="AprsIsClientOptions"/> property.
    /// </summary>
    /// <remarks>
    /// The options are checked with <see cref="AprsIsClientOptions.Validate"/> when they are first used. The
    /// connection is not started: call <see cref="AprsIsConnection.StartAsync"/>. The container disposes it.
    /// </remarks>
    public static IServiceCollection AddAprsIsConnection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<AprsIsClientOptions>().Bind(configuration);
        services.AddSingleton<IConfigureOptions<AprsIsClientOptions>>(new ServerSetting(configuration));
        return services.AddAprsIsConnectionCore();
    }

    /// <summary>
    /// Registers a singleton <see cref="AprsIsConnection"/> and its <see cref="AprsIsClientOptions"/>, set by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// The options are checked with <see cref="AprsIsClientOptions.Validate"/> when they are first used. The
    /// connection is not started: call <see cref="AprsIsConnection.StartAsync"/>. The container disposes it.
    /// </remarks>
    public static IServiceCollection AddAprsIsConnection(
        this IServiceCollection services,
        Action<AprsIsClientOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<AprsIsClientOptions>().Configure(configure);
        return services.AddAprsIsConnectionCore();
    }

    private static IServiceCollection AddAprsIsConnectionCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AprsIsClientOptions>, AprsIsClientOptionsValidator>()
        );

        // The clock comes from the container unless configuration chose one, so tests can swap in a fake.
        services
            .AddOptions<AprsIsClientOptions>()
            .PostConfigure<TimeProvider>(
                (options, time) =>
                {
                    if (options.TimeProvider == TimeProvider.System)
                    {
                        options.TimeProvider = time;
                    }
                }
            );
        services.TryAddSingleton(provider => new AprsIsConnection(
            provider.GetRequiredService<IOptions<AprsIsClientOptions>>().Value,
            provider.GetService<ILoggerFactory>()?.CreateLogger<AprsIsConnection>()
        ));
        return services;
    }
}
