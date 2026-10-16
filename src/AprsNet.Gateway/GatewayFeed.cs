using System.Collections.Concurrent;
using AprsNet.Packets;

namespace AprsNet.Gateway;

/// <summary>
/// Feeds an <see cref="AprsIsServer"/> from any number of packet sources, such as an APRS-IS connection and KISS TNCs:
/// each packet is published once, however many sources heard it within the duplicate window.
/// </summary>
/// <remarks>
/// <para>
/// A source is any stream of packets under a name the caller chooses, such as
/// <c>AprsIsConnection.ReadPacketsAsync</c> or <c>KissTcpConnection.ReadPacketsAsync</c>. Sources can be added and
/// removed while the feed runs; one that ends or fails ends alone, and the task <see cref="AddSource"/> returned says
/// how. See <see cref="PacketMerger"/>, which this uses.
/// </para>
/// <para>
/// Each packet is counted under its source in <see cref="GetSourceCounts"/> and in the gateway's metrics
/// (<c>aprsnet.gateway.packets.received</c> and <c>aprsnet.gateway.packets.duplicates</c>, tagged with the server and
/// the source). Source names become metric tag values, so keep them few and fixed, such as <c>aprs-is</c> and
/// <c>tnc1</c>.
/// </para>
/// </remarks>
public sealed class GatewayFeed : IAsyncDisposable
{
    private readonly AprsIsServer _server;
    private readonly PacketMerger _merger;
    private readonly ConcurrentDictionary<string, Counts> _counts = new(StringComparer.Ordinal);
    private readonly Task _publishing;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a feed for <paramref name="server"/>, which it publishes to until disposed.</summary>
    /// <param name="server">The server to publish to. The feed does not start or dispose it.</param>
    /// <param name="duplicateWindow">How long a packet counts as a duplicate of an earlier copy; 30 seconds by default.</param>
    /// <param name="timeProvider">The clock for the duplicate window; the server's clock by default.</param>
    public GatewayFeed(AprsIsServer server, TimeSpan? duplicateWindow = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
        var time = timeProvider ?? server.TimeProvider;
        _timeProvider = time;
        _merger = new PacketMerger(new DuplicateFilter(duplicateWindow, timeProvider: time), timeProvider: time);
        _publishing = PublishAsync();
    }

    /// <summary>The names of the sources being read now.</summary>
    public IReadOnlyCollection<string> Sources => _merger.Sources;

    /// <summary>Packets published so far.</summary>
    public long Published =>
        _counts.Values.Sum(c => Interlocked.Read(ref c.Packets) - Interlocked.Read(ref c.Duplicates));

    /// <summary>Packets dropped so far as duplicates of a packet another source, or the same one, had already heard.</summary>
    public long Duplicates => _counts.Values.Sum(c => Interlocked.Read(ref c.Duplicates));

    /// <summary>
    /// Starts reading <paramref name="packets"/> as the source <paramref name="name"/>. The task completes when the
    /// source ends: normally when its stream ends or it is removed, faulted when its stream throws.
    /// </summary>
    /// <exception cref="ArgumentException">A source with that name is already being read, or the name is empty.</exception>
    /// <exception cref="ObjectDisposedException">The feed has been disposed.</exception>
    public Task AddSource(string name, IAsyncEnumerable<AprsPacket> packets)
    {
        var reading = _merger.Add(name, packets);
        _counts.GetOrAdd(name, _ => new Counts());
        return reading;
    }

    /// <summary>
    /// Stops reading the source <paramref name="name"/>, and returns once it has stopped. Returns
    /// <see langword="false"/> if no source has that name. Its counts are kept.
    /// </summary>
    public Task<bool> RemoveSourceAsync(string name) => _merger.RemoveAsync(name);

    /// <summary>
    /// What each source has delivered so far, by name, including sources that have ended: every packet it read, and
    /// how many of those were duplicates.
    /// </summary>
    public IReadOnlyDictionary<string, GatewaySourceCounts> GetSourceCounts() =>
        _counts.ToDictionary(
            p => p.Key,
            p => new GatewaySourceCounts(
                Interlocked.Read(ref p.Value.Packets),
                Interlocked.Read(ref p.Value.Duplicates)
            ),
            StringComparer.Ordinal
        );

    /// <summary>When the source <paramref name="name"/> last delivered a packet, duplicates included.</summary>
    internal DateTimeOffset? LastPacketAt(string name) =>
        _counts.TryGetValue(name, out var counts) && Interlocked.Read(ref counts.LastTicks) is var ticks and > 0
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>Stops every source and publishes what they had already read.</summary>
    public async ValueTask DisposeAsync()
    {
        await _merger.DisposeAsync().ConfigureAwait(false);
        await _publishing.ConfigureAwait(false);
    }

    private async Task PublishAsync()
    {
        // Yield first, so the constructor returns before the loop starts.
        await Task.Yield();
        await foreach (var merged in _merger.ReadAllAsync().ConfigureAwait(false))
        {
            var counts = _counts.GetOrAdd(merged.Source, _ => new Counts());
            Interlocked.Increment(ref counts.Packets);
            Interlocked.Exchange(ref counts.LastTicks, _timeProvider.GetUtcNow().UtcTicks);
            GatewayMetrics.Received(_server.Name, merged.Source);
            if (merged.IsDuplicate)
            {
                Interlocked.Increment(ref counts.Duplicates);
                GatewayMetrics.Duplicate(_server.Name, merged.Source);
                continue;
            }

            try
            {
                _server.Publish(merged.Packet);
            }
            catch (ObjectDisposedException)
            {
                // The server stopped before the feed; packets read since have nowhere to go.
            }
        }
    }

    private sealed class Counts
    {
        public long Packets;
        public long Duplicates;
        public long LastTicks;
    }
}

/// <summary>What one source of a <see cref="GatewayFeed"/> has delivered.</summary>
/// <param name="Packets">Every packet the source read.</param>
/// <param name="Duplicates">Packets among them that another source, or the same one, had already heard.</param>
public readonly record struct GatewaySourceCounts(long Packets, long Duplicates)
{
    /// <summary>Packets the source was first to hear, which were published.</summary>
    public long Published => Packets - Duplicates;
}
