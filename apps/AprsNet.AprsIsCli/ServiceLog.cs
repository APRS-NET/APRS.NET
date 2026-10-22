using System.Net;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli;

/// <summary>
/// What <c>serve</c> and <c>tnc share</c> report on stderr, as structured logs in <see cref="Logging.EventCategory"/>,
/// so a service's events reach OpenTelemetry with fields to query on. The stderr lines stay as they are.
/// </summary>
internal static partial class ServiceLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Sharing the TNC at {Tnc} on {Endpoint}, paced for {AirRate} bit/s"
    )]
    public static partial void ShareStarted(this ILogger logger, string tnc, IPEndPoint endpoint, int airRate);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "An app connected from {Remote}")]
    public static partial void AppConnected(this ILogger logger, IPEndPoint remote);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The app at {Remote} disconnected")]
    public static partial void AppDisconnected(this ILogger logger, IPEndPoint remote);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Disconnected the app at {Remote}: it fell too far behind"
    )]
    public static partial void AppFellBehind(this ILogger logger, IPEndPoint remote);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Turned away {Remote}: {MaxClients} apps are already connected"
    )]
    public static partial void AppTurnedAway(this ILogger logger, IPEndPoint remote, int maxClients);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Dropped a frame from an app: {Reason}")]
    public static partial void FrameDropped(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Stopped sharing: heard {Heard} frames, sent {Sent} from apps ({Dropped} dropped); {FellBehind} apps "
            + "dropped for falling behind, {TurnedAway} turned away"
    )]
    public static partial void ShareStopped(
        this ILogger logger,
        long heard,
        long sent,
        long dropped,
        long fellBehind,
        long turnedAway
    );

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Lost the TNC at {Tnc}: {Reason}; reconnecting in {RetrySeconds}s"
    )]
    public static partial void TncLost(this ILogger logger, string tnc, string reason, double retrySeconds);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Reconnected to the TNC at {Tnc}")]
    public static partial void TncReconnected(this ILogger logger, string tnc);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "The TNC at {Tnc}: {Warning}")]
    public static partial void TncWarning(this ILogger logger, string tnc, string warning);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "The TNC at {Tnc}: {Warning}")]
    public static partial void TncWarningOver(this ILogger logger, string tnc, string warning);

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Information,
        Message = "Serving {Sources} on {Endpoint} as {ServerName}"
    )]
    public static partial void ServeStarted(
        this ILogger logger,
        string sources,
        IPEndPoint endpoint,
        string serverName
    );

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Information,
        Message = "Gating to APRS-IS as {Callsign} (receive-only), beaconing every {BeaconInterval}"
    )]
    public static partial void IGateStarted(this ILogger logger, string callsign, string beaconInterval);

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Information,
        Message = "Stopped serving: served {Published} packets ({Duplicates} duplicates dropped); {FellBehind} clients "
            + "dropped for falling behind"
    )]
    public static partial void ServeStopped(this ILogger logger, long published, long duplicates, long fellBehind);
}

/// <summary>Each frame or packet <c>serve</c> and <c>tnc share</c> pass on, for <c>--log-packets</c>.</summary>
internal static partial class ServicePacketLog
{
    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Heard from {Source}: {Packet}")]
    public static partial void FrameHeard(this ILogger logger, string source, string packet);

    [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "Sent for an app from {Source}: {Packet}")]
    public static partial void FrameSent(this ILogger logger, string source, string packet);

    [LoggerMessage(EventId = 32, Level = LogLevel.Information, Message = "Packet via {Via} from {Source}: {Packet}")]
    public static partial void PacketServed(this ILogger logger, string via, string source, string packet);
}
