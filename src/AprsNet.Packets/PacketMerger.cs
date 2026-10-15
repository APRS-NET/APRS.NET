using System.Threading.Channels;

namespace AprsNet.Packets;

/// <summary>
/// Reads packets from several sources at once, such as an APRS-IS connection and KISS TNCs, into one stream in which
/// each packet carries the name of its source, and a packet heard again on any source within the duplicate window is
/// marked as a duplicate of the first copy.
/// </summary>
/// <remarks>
/// <para>
/// A source is any stream of packets, such as <c>AprsIsConnection.ReadPacketsAsync</c>,
/// <c>KissTcpConnection.ReadPacketsAsync</c> or <c>KissStreamClient.ReadPacketsAsync</c>, under a name the caller
/// chooses. Sources can be added while the merger runs. Each one ends on its own: when its stream ends, faults or is
/// removed, the task <see cref="Add"/> returned completes, and the other sources carry on.
/// </para>
/// <para>
/// Duplicates are decided in the order packets arrive, with a <see cref="DuplicateFilter"/> (30 seconds by default).
/// Duplicates are passed on, not dropped, so a caller can show where else a packet was heard. Read the stream with one
/// reader; when the reader falls behind by <c>capacity</c> packets, the sources wait.
/// </para>
/// </remarks>
public sealed class PacketMerger : IAsyncDisposable
{
    private readonly DuplicateFilter _duplicates;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<MergedPacket> _channel;
    private readonly Dictionary<string, (CancellationTokenSource Stop, Task Pump)> _sources = new(
        StringComparer.Ordinal
    );
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>Creates a merger.</summary>
    /// <param name="duplicates">The duplicate window and memory to use; a new <see cref="DuplicateFilter"/> if none.</param>
    /// <param name="capacity">How many packets may wait for the reader before the sources wait too; 1024 by default.</param>
    /// <param name="timeProvider">The clock for <see cref="MergedPacket.ReceivedAt"/>; the system clock by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public PacketMerger(DuplicateFilter? duplicates = null, int capacity = 1024, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _duplicates = duplicates ?? new DuplicateFilter(timeProvider: _timeProvider);
        _channel = Channel.CreateBounded<MergedPacket>(
            new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }
        );
    }

    /// <summary>The names of the sources being read now.</summary>
    public IReadOnlyCollection<string> Sources
    {
        get
        {
            lock (_lock)
            {
                return [.. _sources.Keys];
            }
        }
    }

    /// <summary>
    /// Starts reading <paramref name="packets"/> as the source <paramref name="source"/>. The task completes when the
    /// source ends: normally when its stream ends or it is removed, faulted when its stream throws.
    /// </summary>
    /// <exception cref="ArgumentException">A source with that name is already being read, or the name is empty.</exception>
    /// <exception cref="ObjectDisposedException">The merger has been disposed.</exception>
    public Task Add(string source, IAsyncEnumerable<AprsPacket> packets)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(packets);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sources.ContainsKey(source))
            {
                throw new ArgumentException($"A source named '{source}' is already being read.", nameof(source));
            }

            var stop = new CancellationTokenSource();
            var pump = PumpAsync(source, packets, stop.Token);
            _sources.Add(source, (stop, pump));
            return pump;
        }
    }

    /// <summary>
    /// Stops reading the source <paramref name="source"/>, and returns once it has stopped. Returns
    /// <see langword="false"/> if no source has that name.
    /// </summary>
    public async Task<bool> RemoveAsync(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        (CancellationTokenSource Stop, Task Pump) entry;
        lock (_lock)
        {
            if (!_sources.Remove(source, out entry))
            {
                return false;
            }
        }

        await entry.Stop.CancelAsync();
        await Quietly(entry.Pump);
        entry.Stop.Dispose();
        return true;
    }

    /// <summary>
    /// The merged packets, in the order they arrived, until the merger is disposed or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public IAsyncEnumerable<MergedPacket> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Stops every source and ends <see cref="ReadAllAsync"/> once the packets already merged are read.</summary>
    public async ValueTask DisposeAsync()
    {
        (CancellationTokenSource Stop, Task Pump)[] sources;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sources = [.. _sources.Values];
            _sources.Clear();
        }

        foreach (var (stop, _) in sources)
        {
            await stop.CancelAsync();
        }

        foreach (var (stop, pump) in sources)
        {
            await Quietly(pump);
            stop.Dispose();
        }

        _channel.Writer.TryComplete();
    }

    private async Task PumpAsync(string source, IAsyncEnumerable<AprsPacket> packets, CancellationToken stop)
    {
        // Yield first, so Add returns before the source is read.
        await Task.Yield();
        try
        {
            await foreach (var packet in packets.WithCancellation(stop))
            {
                var check = _duplicates.Check(packet, source);
                var duplicateOf = check.IsDuplicate
                    ? new MergedPacketOrigin(check.FirstSource ?? "", check.FirstSeen)
                    : null;
                var merged = new MergedPacket(packet, source, _timeProvider.GetUtcNow(), duplicateOf);
                await _channel.Writer.WriteAsync(merged, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Removed, or the merger was disposed.
        }
        catch (ChannelClosedException)
        {
            // The merger was disposed while this source waited to write.
        }
        finally
        {
            lock (_lock)
            {
                if (_sources.TryGetValue(source, out var entry) && entry.Stop.Token == stop)
                {
                    _sources.Remove(source);
                    entry.Stop.Dispose();
                }
            }
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // A source's own fault is reported through the task Add returned.
        }
    }
}

/// <summary>A packet from a <see cref="PacketMerger"/>, with where it was heard.</summary>
/// <param name="Packet">The packet.</param>
/// <param name="Source">The name of the source it was read from.</param>
/// <param name="ReceivedAt">When the merger read it.</param>
/// <param name="DuplicateOf">
/// For a copy of a packet already heard within the duplicate window, where and when the first copy was heard;
/// <see langword="null"/> for a new packet.
/// </param>
public sealed record MergedPacket(
    AprsPacket Packet,
    string Source,
    DateTimeOffset ReceivedAt,
    MergedPacketOrigin? DuplicateOf
)
{
    /// <summary>Whether this is a copy of a packet already heard within the duplicate window.</summary>
    public bool IsDuplicate => DuplicateOf is not null;
}

/// <summary>Where and when the first copy of a duplicated packet was heard.</summary>
/// <param name="Source">The source of the first copy.</param>
/// <param name="ReceivedAt">When the first copy was heard.</param>
public sealed record MergedPacketOrigin(string Source, DateTimeOffset ReceivedAt);
