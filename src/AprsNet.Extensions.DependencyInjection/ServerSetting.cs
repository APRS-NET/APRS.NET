using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AprsNet.Extensions.DependencyInjection;

/// <summary>
/// Reads the <c>Server</c> key into <see cref="AprsIsClientOptions.Endpoint"/>: a region key such as <c>noam</c>, or
/// <c>host[:port]</c>. An unreadable value fails when the options are first used, naming the key.
/// </summary>
internal sealed class ServerSetting(IConfiguration configuration) : IConfigureOptions<AprsIsClientOptions>
{
    public const string Key = "Server";

    public void Configure(AprsIsClientOptions options)
    {
        if (configuration[Key] is not { } server)
        {
            return;
        }

        if (
            AprsIsServerPreset.All.FirstOrDefault(p =>
                p.Key.Equals(server.Trim(), StringComparison.OrdinalIgnoreCase)
            ) is
            { } preset
        )
        {
            options.Endpoint = preset.Endpoint;
        }
        else if (AprsIsEndpoint.TryParse(server, out var endpoint))
        {
            options.Endpoint = endpoint;
        }
        else
        {
            var path = configuration is IConfigurationSection section ? $"{section.Path}:{Key}" : Key;
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(AprsIsClientOptions),
                [$"{path} '{server}' is not a region key or host[:port]."]
            );
        }
    }
}
