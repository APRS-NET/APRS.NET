using System.Collections.Concurrent;

namespace AprsNet.Gateway;

/// <summary>
/// Whether a gateway is serving: each of its sources (an APRS-IS connection, a TNC) connected or not, and when each last
/// delivered a packet. <see cref="GatewayWebServer"/> serves it at <c>/health</c> for a monitor or a container
/// orchestrator.
/// </summary>
/// <remarks>
/// Register each source with a check that says whether it is connected now, such as
/// <c>() =&gt; connection.State == AprsIsConnectionState.Connected</c> or <c>() =&gt; tnc.IsConnected</c>. With a
/// <see cref="GatewayFeed"/>, the report also gives when each source last delivered a packet, under the same name.
/// </remarks>
public sealed class GatewayHealth
{
    private readonly ConcurrentDictionary<string, Func<bool>> _sources = new(StringComparer.Ordinal);
    private readonly GatewayFeed? _feed;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a health check for a gateway.</summary>
    /// <param name="feed">The gateway's feed, for when each source last delivered a packet.</param>
    /// <param name="timeProvider">The clock for <see cref="GatewayHealthReport.CheckedAt"/>.</param>
    public GatewayHealth(GatewayFeed? feed = null, TimeProvider? timeProvider = null)
    {
        _feed = feed;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Adds a source, with a check that says whether it is connected now. Keep the check quick.</summary>
    /// <exception cref="ArgumentException">A source with that name is already added, or the name is empty.</exception>
    public void AddSource(string name, Func<bool> isConnected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(isConnected);
        if (!_sources.TryAdd(name, isConnected))
        {
            throw new ArgumentException($"A source named '{name}' is already added.", nameof(name));
        }
    }

    /// <summary>Stops checking the source <paramref name="name"/>; returns whether there was one.</summary>
    public bool RemoveSource(string name) => _sources.TryRemove(name, out _);

    /// <summary>
    /// Checks every source now. Healthy when all are connected (or there are none), degraded when some are, and
    /// unhealthy when none are. A check that throws counts as not connected.
    /// </summary>
    public GatewayHealthReport Check()
    {
        var sources = new SortedDictionary<string, GatewaySourceHealth>(StringComparer.Ordinal);
        foreach (var (name, isConnected) in _sources)
        {
            bool connected;
            try
            {
                connected = isConnected();
            }
            catch (Exception)
            {
                connected = false;
            }

            sources[name] = new GatewaySourceHealth(connected, _feed?.LastPacketAt(name));
        }

        var up = sources.Values.Count(s => s.IsConnected);
        var status =
            up == sources.Count ? GatewayHealthStatus.Healthy
            : up > 0 ? GatewayHealthStatus.Degraded
            : GatewayHealthStatus.Unhealthy;
        return new GatewayHealthReport(status, sources, _timeProvider.GetUtcNow());
    }
}

/// <summary>How a gateway is doing.</summary>
public enum GatewayHealthStatus
{
    /// <summary>Every source is connected.</summary>
    Healthy,

    /// <summary>Some sources are connected and some aren't: it serves, but not everything.</summary>
    Degraded,

    /// <summary>No source is connected: it serves nothing new.</summary>
    Unhealthy,
}

/// <summary>What <see cref="GatewayHealth.Check"/> found.</summary>
/// <param name="Status">The overall status.</param>
/// <param name="Sources">Each source by name.</param>
/// <param name="CheckedAt">When the check ran.</param>
public sealed record GatewayHealthReport(
    GatewayHealthStatus Status,
    IReadOnlyDictionary<string, GatewaySourceHealth> Sources,
    DateTimeOffset CheckedAt
);

/// <summary>One source's health.</summary>
/// <param name="IsConnected">Whether it is connected now.</param>
/// <param name="LastPacket">When it last delivered a packet to the feed, if it has and a feed was given.</param>
public readonly record struct GatewaySourceHealth(bool IsConnected, DateTimeOffset? LastPacket);
