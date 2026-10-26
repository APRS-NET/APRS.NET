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
    private readonly ConcurrentDictionary<
        string,
        (Func<bool> IsConnected, Func<bool>? IsQuiet, Func<bool>? IsProtocolMismatch)
    > _sources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<long>> _counters = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Whether a quiet source counts as down, so a gateway whose TNCs have all gone quiet reports
    /// <see cref="GatewayHealthStatus.Unhealthy"/>. <see langword="false"/> by default: a quiet source is reported as
    /// such but still counts as up, since a quiet channel looks the same as a TNC switched off. Turn it on when the
    /// quiet period is longer than the channel is ever quiet, such as a TNC's <c>QuietWarning</c> of 30 minutes. A
    /// source counts as up again as soon as its quiet check says it isn't.
    /// </summary>
    public bool CountQuietAsDown { get; init; }

    /// <summary>Adds a source, with a check that says whether it is connected now. Keep the check quick.</summary>
    /// <exception cref="ArgumentException">A source with that name is already added, or the name is empty.</exception>
    public void AddSource(string name, Func<bool> isConnected) => AddSource(name, isConnected, null);

    /// <summary>
    /// Adds a source, with a check that says whether it is connected now, and one that says whether it has gone quiet,
    /// such as a TNC's <c>KissWarning.Quiet</c> warning. A quiet source is reported as such, and
    /// counts as down only with <see cref="CountQuietAsDown"/>. Keep the checks quick.
    /// </summary>
    /// <exception cref="ArgumentException">A source with that name is already added, or the name is empty.</exception>
    public void AddSource(string name, Func<bool> isConnected, Func<bool>? isQuiet) =>
        AddSource(name, isConnected, isQuiet, null);

    /// <summary>
    /// Adds a source with the checks above, and one that says whether it speaks the wrong protocol, such as a TNC's
    /// <c>KissWarning.TextNotKiss</c> warning (text, not KISS frames). Such a source is connected, but nothing it sends
    /// can be read, so it doesn't count as up. Keep the checks quick.
    /// </summary>
    /// <exception cref="ArgumentException">A source with that name is already added, or the name is empty.</exception>
    public void AddSource(string name, Func<bool> isConnected, Func<bool>? isQuiet, Func<bool>? isProtocolMismatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(isConnected);
        if (!_sources.TryAdd(name, (isConnected, isQuiet, isProtocolMismatch)))
        {
            throw new ArgumentException($"A source named '{name}' is already added.", nameof(name));
        }
    }

    /// <summary>Stops checking the source <paramref name="name"/>; returns whether there was one.</summary>
    public bool RemoveSource(string name) => _sources.TryRemove(name, out _);

    /// <summary>
    /// Adds a count to report with every check, such as the packets an IGate has gated (<c>igate.gated</c>). It says
    /// what the gateway has done, and doesn't change the status. Keep the read quick.
    /// </summary>
    /// <exception cref="ArgumentException">A counter with that name is already added, or the name is empty.</exception>
    public void AddCounter(string name, Func<long> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(read);
        if (!_counters.TryAdd(name, read))
        {
            throw new ArgumentException($"A counter named '{name}' is already added.", nameof(name));
        }
    }

    /// <summary>
    /// Checks every source now. Healthy when all are up (or there are none), degraded when some are, and unhealthy when
    /// none are. A source is up when it is connected, not speaking the wrong protocol and, with
    /// <see cref="CountQuietAsDown"/>, not quiet. A check that throws counts as
    /// not connected, not quiet and no mismatch; a counter whose read throws is left out.
    /// </summary>
    public GatewayHealthReport Check()
    {
        var sources = new SortedDictionary<string, GatewaySourceHealth>(StringComparer.Ordinal);
        foreach (var (name, (isConnected, isQuiet, isProtocolMismatch)) in _sources)
        {
            sources[name] = new GatewaySourceHealth(Try(isConnected), _feed?.LastPacketAt(name))
            {
                IsQuiet = isQuiet is not null && Try(isQuiet),
                IsProtocolMismatch = isProtocolMismatch is not null && Try(isProtocolMismatch),
            };
        }

        var up = sources.Values.Count(s => s.IsConnected && !s.IsProtocolMismatch && !(CountQuietAsDown && s.IsQuiet));
        var status =
            up == sources.Count ? GatewayHealthStatus.Healthy
            : up > 0 ? GatewayHealthStatus.Degraded
            : GatewayHealthStatus.Unhealthy;
        var counters = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (name, read) in _counters)
        {
            try
            {
                counters[name] = read();
            }
            catch (Exception)
            {
                // Left out, as a source check that throws counts as down.
            }
        }

        return new GatewayHealthReport(status, sources, _timeProvider.GetUtcNow()) { Counters = counters };
    }

    // A check that throws counts as false: not connected, not known to be quiet, no known mismatch.
    private static bool Try(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>How a gateway is doing.</summary>
public enum GatewayHealthStatus
{
    /// <summary>
    /// Every source is up: connected, speaking the protocol expected and, with
    /// <see cref="GatewayHealth.CountQuietAsDown"/>, not quiet.
    /// </summary>
    Healthy,

    /// <summary>Some sources are up and some aren't: it serves, but not everything.</summary>
    Degraded,

    /// <summary>No source is up: it serves nothing new.</summary>
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
)
{
    /// <summary>Each counter added with <see cref="GatewayHealth.AddCounter"/>, by name; empty when there are none.</summary>
    public IReadOnlyDictionary<string, long> Counters { get; init; } = new Dictionary<string, long>();
}

/// <summary>One source's health.</summary>
/// <param name="IsConnected">Whether it is connected now.</param>
/// <param name="LastPacket">When it last delivered a packet to the feed, if it has and a feed was given.</param>
public readonly record struct GatewaySourceHealth(bool IsConnected, DateTimeOffset? LastPacket)
{
    /// <summary>
    /// Whether it has gone quiet: connected, but nothing has arrived for a while. Only known for a source added with a
    /// quiet check.
    /// </summary>
    public bool IsQuiet { get; init; }

    /// <summary>
    /// Whether it is connected but speaking the wrong protocol, such as a TNC sending text instead of KISS frames, so
    /// nothing it sends can be read. Only known for a source added with a mismatch check.
    /// </summary>
    public bool IsProtocolMismatch { get; init; }
}
