using System.Globalization;
using System.Text;
using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>
/// Answers APRS queries (APRS101 ch. 15) for one station: the general queries every station hears (<c>?APRS?</c>,
/// <c>?IGATE?</c>, <c>?WX?</c>) and the queries directed at it (<c>?APRSP</c>, <c>?APRSS</c>, <c>?APRSD</c>,
/// <c>?APRSH</c>, <c>?APRSM</c>, <c>?APRSO</c>, <c>?APRST</c> and <c>?PING?</c>). It does no I/O: give it each packet
/// heard with the station's <see cref="QueryState"/>, and send the payloads of the <see cref="QueryAnswer"/> it
/// returns after its <see cref="QueryAnswer.Delay"/>, from the station's own <see cref="PacketAddress"/>.
/// </summary>
/// <remarks>
/// <para>
/// A general query reaches every station at once, so each answers after a random wait between
/// <see cref="QueryResponderOptions.MinimumDelay"/> and <see cref="QueryResponderOptions.MaximumDelay"/>, and only when
/// the query's footprint, if it has one, includes the station. Directed queries are answered straight away.
/// </para>
/// <para>
/// The same query heard again within <see cref="QueryResponderOptions.RepeatWindow"/>, digipeated or through an
/// IGate, is answered once. Queries are never acknowledged, and the messages answering them carry no message ID, except
/// the waiting messages an <c>?APRSM</c> sends again. Queries the station doesn't know, and queries it has nothing to
/// answer with, return <see langword="null"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var responder = new QueryResponder(address.Source);
/// if (responder.Answer(packet, new QueryState { Position = beacon, Status = status }) is { } answer)
/// {
///     await Task.Delay(answer.Delay, cancellationToken);
///     foreach (var payload in answer.Payloads)
///     {
///         await SendAsync(address.Create(payload), cancellationToken);
///     }
/// }
/// </code>
/// </example>
public sealed class QueryResponder
{
    private const int MaxMessageText = 67;
    private const int HeardHours = 8;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Querier, QueryKind Kind, string? Argument), long> _answered = [];

    /// <summary>Creates a responder for <paramref name="station"/>, the callsign directed queries are addressed to.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public QueryResponder(Callsign station, QueryResponderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(station);
        options ??= new QueryResponderOptions();
        options.Validate();
        Station = station;
        MinimumDelay = options.MinimumDelay;
        MaximumDelay = options.MaximumDelay;
        RepeatWindow = options.RepeatWindow;
        _timeProvider = options.TimeProvider;
        _random = options.Random;
    }

    /// <summary>The station whose queries are answered.</summary>
    public Callsign Station { get; }

    /// <summary>The shortest wait before answering a general query.</summary>
    public TimeSpan MinimumDelay { get; }

    /// <summary>The longest wait before answering a general query.</summary>
    public TimeSpan MaximumDelay { get; }

    /// <summary>How long the same query from the same station is answered only once.</summary>
    public TimeSpan RepeatWindow { get; }

    /// <summary>
    /// The answer to <paramref name="packet"/> if it is a query for this station, or <see langword="null"/>: not a
    /// query, a directed query for another station, a query it doesn't know, one whose footprint leaves the station
    /// out, one already answered, or one <paramref name="state"/> has nothing for. A query that came through an IGate
    /// as third-party traffic is answered to the station that sent it.
    /// </summary>
    /// <param name="packet">A packet heard.</param>
    /// <param name="state">What the station has to answer with.</param>
    public QueryAnswer? Answer(AprsPacket packet, QueryState state)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(state);
        if (packet.Payload is ThirdPartyPayload thirdParty)
        {
            packet = thirdParty.Packet;
        }

        if (
            packet.Source is not { } querier
            || querier.Value.Length > 9 // no addressee can hold it
            || querier.Value.Equals(Station.Value, StringComparison.OrdinalIgnoreCase) // our own query
        )
        {
            return null;
        }

        var (kind, argument, general) = packet.Payload switch
        {
            QueryReport query => (query.Kind, query.Argument, true),
            MessagePayload { Kind: MessageKind.Query } message
                when message.Addressee.Equals(Station.Value, StringComparison.OrdinalIgnoreCase) => (
                message.QueryKind,
                message.QueryArgument,
                false
            ),
            _ => (QueryKind.Unknown, null, false),
        };

        if (kind == QueryKind.Unknown || general && !IsInside(packet.Payload as QueryReport, state))
        {
            return null;
        }

        var payloads = Payloads(kind, querier, argument, packet, state);
        if (payloads.Count == 0 || !FirstTime(querier, kind, argument))
        {
            return null;
        }

        return new QueryAnswer(querier, kind, general, general ? RandomDelay() : TimeSpan.Zero, payloads);
    }

    /// <summary>Whether a general query's footprint includes the station: always without one.</summary>
    private static bool IsInside(QueryReport? query, QueryState state) =>
        query switch
        {
            { Footprint: { } footprint } => state.Position is { } position && footprint.Contains(position.Position),
            { Argument: not null } => false, // an area that couldn't be read
            _ => true,
        };

    private List<AprsPayload> Payloads(
        QueryKind kind,
        Callsign querier,
        string? argument,
        AprsPacket packet,
        QueryState state
    )
    {
        List<AprsPayload> payloads = [];
        switch (kind)
        {
            case QueryKind.PositionAndStatus:
                Add(payloads, state.Position);
                Add(payloads, state.Status);
                break;
            case QueryKind.Position:
                Add(payloads, state.Position);
                break;
            case QueryKind.Status:
                Add(payloads, state.Status);
                break;
            case QueryKind.IGate:
                Add(payloads, state.Capabilities);
                break;
            case QueryKind.Weather:
                // APRS101: a weather report, and the station's position when the report doesn't carry it.
                Add(payloads, state.Weather);
                if (state.Weather is WeatherReport)
                {
                    Add(payloads, state.Position);
                }

                break;
            case QueryKind.Objects:
                payloads.AddRange(state.Objects);
                break;
            case QueryKind.Messages:
                payloads.AddRange(state.MessagesWaiting?.Invoke(querier) ?? []);
                break;
            case QueryKind.HeardDirect:
                payloads.AddRange(HeardDirect(querier, state.HeardDirect));
                break;
            case QueryKind.Heard:
                if (Callsign.TryParse(argument?.Trim(), out var heard) && state.Heard?.Invoke(heard) is { } station)
                {
                    payloads.AddRange(Heard(querier, heard, station));
                }

                break;
            case QueryKind.Trace:
                payloads.Add(Reply(querier, Trace(packet)));
                break;
        }

        return payloads;
    }

    private static void Add(List<AprsPayload> payloads, AprsPayload? payload)
    {
        if (payload is not null)
        {
            payloads.Add(payload);
        }
    }

    /// <summary><c>Directs= WA1LOU WD5IVD …</c>, in as many messages as the callsigns need.</summary>
    private static IEnumerable<MessagePayload> HeardDirect(Callsign querier, IReadOnlyList<Callsign> heard)
    {
        var text = new StringBuilder("Directs=");
        var any = false;
        foreach (var station in heard)
        {
            if (text.Length + 1 + station.Value.Length > MaxMessageText && any)
            {
                yield return Reply(querier, text.ToString());
                text.Clear().Append("Directs=");
                any = false;
            }

            text.Append(' ').Append(station.Value);
            any = true;
        }

        if (any)
        {
            yield return Reply(querier, text.ToString());
        }
    }

    /// <summary>
    /// <c>N0QBF HEARD: 1 3 2 . . 4 5 6</c>, packets in each of the last 8 hours with <c>.</c> for none, and the
    /// station's position as an object.
    /// </summary>
    private IEnumerable<AprsPayload> Heard(Callsign querier, Callsign heard, HeardStation station)
    {
        var counts = Enumerable
            .Range(0, HeardHours)
            .Select(hour =>
                hour < station.PacketsPerHour.Count && station.PacketsPerHour[hour] > 0
                    ? station.PacketsPerHour[hour].ToString(CultureInfo.InvariantCulture)
                    : "."
            );
        if (station.Position is { } position)
        {
            yield return new ObjectReport
            {
                Name = heard.Value,
                Position = position,
                Symbol = station.Symbol,
                Timestamp = AprsTimestamp.FromDateTimeOffset(station.LastHeard ?? _timeProvider.GetUtcNow()),
            };
        }

        yield return Reply(querier, $"{heard.Value} HEARD: {string.Join(' ', counts)}");
    }

    /// <summary>
    /// The query's header as heard, <c>N8UR&gt;APRS,DIGI1,WIDE*:</c>: the path by which it reached the station. Path
    /// entries that don't fit a message are left off the end.
    /// </summary>
    private static string Trace(AprsPacket packet)
    {
        var header = $"{packet.Source}>{packet.Destination}";
        foreach (var entry in packet.Path ?? [])
        {
            var longer = $"{header},{entry}";
            if (longer.Length + 1 > MaxMessageText)
            {
                break;
            }

            header = longer;
        }

        return header + ":";
    }

    private static MessagePayload Reply(Callsign querier, string text) =>
        new() { Addressee = querier.Value, Text = text };

    /// <summary>Whether this query hasn't been answered within the repeat window; if so, remembers it now.</summary>
    private bool FirstTime(Callsign querier, QueryKind kind, string? argument)
    {
        var key = (querier.Value.ToUpperInvariant(), kind, argument?.Trim().ToUpperInvariant());
        var now = _timeProvider.GetTimestamp();
        lock (_lock)
        {
            foreach (var old in _answered.Where(a => _timeProvider.GetElapsedTime(a.Value, now) >= RepeatWindow))
            {
                _answered.Remove(old.Key);
            }

            return _answered.TryAdd(key, now);
        }
    }

    private TimeSpan RandomDelay()
    {
        double fraction;
        lock (_lock)
        {
            fraction = _random.NextDouble();
        }

        return MinimumDelay + TimeSpan.FromTicks((long)(fraction * (MaximumDelay - MinimumDelay).Ticks));
    }
}

/// <summary>Settings for a <see cref="QueryResponder"/>.</summary>
public sealed class QueryResponderOptions
{
    /// <summary>
    /// The shortest wait before answering a general query: 2 seconds by default, so the query's own digipeated copies
    /// clear the channel first.
    /// </summary>
    public TimeSpan MinimumDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest wait before answering a general query: 60 seconds by default. Every station hearing it picks a time
    /// in between at random, so their answers don't collide.
    /// </summary>
    public TimeSpan MaximumDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the same query from the same station is answered only once, however many copies arrive: 2 minutes by
    /// default, longer than the copies of one query take to come in.
    /// </summary>
    public TimeSpan RepeatWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The clock for the repeat window and timestamps. The system clock by default.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>The random source for the waits. <see cref="System.Random.Shared"/> by default; tests can seed one.</summary>
    public Random Random { get; set; } = Random.Shared;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumDelay, TimeSpan.Zero, nameof(MinimumDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDelay, MinimumDelay, nameof(MaximumDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(RepeatWindow, TimeSpan.Zero, nameof(RepeatWindow));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
        ArgumentNullException.ThrowIfNull(Random, nameof(Random));
    }
}

/// <summary>What a <see cref="QueryResponder"/> answers a query with.</summary>
public sealed class QueryAnswer
{
    internal QueryAnswer(
        Callsign querier,
        QueryKind kind,
        bool isGeneral,
        TimeSpan delay,
        IReadOnlyList<AprsPayload> payloads
    )
    {
        Querier = querier;
        Kind = kind;
        IsGeneral = isGeneral;
        Delay = delay;
        Payloads = payloads;
    }

    /// <summary>The station that sent the query; messages in the answer are addressed to it.</summary>
    public Callsign Querier { get; }

    /// <summary>What the query asked for.</summary>
    public QueryKind Kind { get; }

    /// <summary>Whether it was a general query, to every station, rather than one directed at this station.</summary>
    public bool IsGeneral { get; }

    /// <summary>How long to wait before sending: a random wait for a general query, zero for a directed one.</summary>
    public TimeSpan Delay { get; }

    /// <summary>The payloads to send, in order, from the station's own address.</summary>
    public IReadOnlyList<AprsPayload> Payloads { get; }
}
