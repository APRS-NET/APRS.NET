using System.Diagnostics.CodeAnalysis;

namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// One of the aprs2.net Tier 2 rotates, with a stable key an app can store and a region name it can show. The regions
/// and their wording follow <see href="https://www.aprs2.net/">aprs2.net</see> (accessed 2026-10-14), which also
/// says that users should connect to port 14580, the user-defined filter port the endpoints use.
/// </summary>
public sealed class AprsIsServerPreset
{
    private AprsIsServerPreset(string key, string name, AprsIsEndpoint endpoint)
    {
        Key = key;
        Name = name;
        Endpoint = endpoint;
    }

    /// <summary>
    /// Every preset in a fixed order: the worldwide rotate first, then North America, South America, Europe and
    /// Africa, Asia and Oceania. Each <see cref="Endpoint"/> is the matching <see cref="AprsIsEndpoint"/> preset
    /// property, such as <see cref="AprsIsEndpoint.Europe"/>.
    /// </summary>
    public static IReadOnlyList<AprsIsServerPreset> All { get; } =
    [
        new("rotate", "Worldwide", AprsIsEndpoint.Rotate),
        new("noam", "North America", AprsIsEndpoint.NorthAmerica),
        new("soam", "South America", AprsIsEndpoint.SouthAmerica),
        new("euro", "Europe and Africa", AprsIsEndpoint.Europe),
        new("asia", "Asia", AprsIsEndpoint.Asia),
        new("aunz", "Oceania", AprsIsEndpoint.Oceania),
    ];

    /// <summary>
    /// A stable, lower-case key to store in settings: <c>rotate</c>, <c>noam</c>, <c>soam</c>, <c>euro</c>,
    /// <c>asia</c> or <c>aunz</c>, the first label of the rotate's host name. Keys never change once released.
    /// </summary>
    public string Key { get; }

    /// <summary>A short English region name for display, for example <c>Europe and Africa</c>.</summary>
    public string Name { get; }

    /// <summary>The rotate's host on the user-defined filter port (14580).</summary>
    public AprsIsEndpoint Endpoint { get; }

    /// <summary>
    /// Finds a preset by its <see cref="Key"/> (<c>euro</c>), or by a server as an app might have stored it
    /// (<c>euro.aprs2.net</c>, <c>euro.aprs2.net:14580</c>). Matching ignores case and surrounding whitespace. For a
    /// server the port is ignored, so the full feed port still finds its region; a host that is not one of the
    /// rotates finds nothing.
    /// </summary>
    public static bool TryFind(
        [NotNullWhen(true)] string? keyOrServer,
        [NotNullWhen(true)] out AprsIsServerPreset? preset
    )
    {
        preset = null;
        if (string.IsNullOrWhiteSpace(keyOrServer))
        {
            return false;
        }

        var text = keyOrServer.Trim();
        preset = All.FirstOrDefault(p => p.Key.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (preset is null && AprsIsEndpoint.TryParse(text, out var endpoint))
        {
            TryFind(endpoint, out preset);
        }

        return preset is not null;
    }

    /// <summary>Finds the preset whose host matches <paramref name="endpoint"/>'s, ignoring case and the port.</summary>
    public static bool TryFind(
        [NotNullWhen(true)] AprsIsEndpoint? endpoint,
        [NotNullWhen(true)] out AprsIsServerPreset? preset
    )
    {
        var host = endpoint?.Host.TrimEnd('.'); // a fully qualified "euro.aprs2.net." is the same host
        preset = host is null
            ? null
            : All.FirstOrDefault(p => p.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
        return preset is not null;
    }

    /// <summary>The region name, for example <c>Europe and Africa</c>.</summary>
    public override string ToString() => Name;
}
