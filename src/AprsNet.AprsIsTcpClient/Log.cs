using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Source-generated log messages. The passcode is never passed to any of them.</summary>
internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Connecting to {Endpoint} as {Callsign} ({Mode}), filter: {Filter}"
    )]
    public static partial void Connecting(
        this ILogger logger,
        AprsIsEndpoint endpoint,
        string callsign,
        string mode,
        string filter
    );

    [LoggerMessage(Level = LogLevel.Information, Message = "Login {Status} as {Callsign} on server {Server}")]
    public static partial void LoggedIn(this ILogger logger, LoginStatus status, string callsign, string? server);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A non-read-only login was reported unverified; the passcode is probably wrong, so this login cannot transmit"
    )]
    public static partial void PasscodeNotAccepted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server rejected the login as {Callsign}: {ServerMessage}")]
    public static partial void LoginRejected(this ILogger logger, string callsign, string serverMessage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server: {Line}")]
    public static partial void ServerLine(this ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filter set: {Filter}")]
    public static partial void FilterSet(this ILogger logger, string filter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discarded a line of {Length} bytes (limit {Limit})")]
    public static partial void OverlongLineDiscarded(this ILogger logger, int limit, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Receive queue full; dropped the oldest line")]
    public static partial void QueueFull(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server closed the connection")]
    public static partial void ServerClosed(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Receive loop failed")]
    public static partial void ReceiveFailed(this ILogger logger, Exception exception);
}
