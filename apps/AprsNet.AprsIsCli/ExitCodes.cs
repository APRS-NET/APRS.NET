namespace AprsNet.AprsIsCli;

/// <summary>Process exit codes. Scripts can retry on <see cref="ConnectionFailed"/> and stop on <see cref="Usage"/> or <see cref="LoginFailed"/>.</summary>
internal static class ExitCodes
{
    /// <summary>Finished cleanly: Ctrl+C, <c>--count</c> reached, or <c>--duration</c> elapsed.</summary>
    public const int Success = 0;

    /// <summary>Unexpected error.</summary>
    public const int Error = 1;

    /// <summary>The connection failed or dropped.</summary>
    public const int ConnectionFailed = 2;

    /// <summary>Bad or conflicting options.</summary>
    public const int Usage = 3;

    /// <summary>The login was rejected or timed out, or <c>send</c> was not given a verified login.</summary>
    public const int LoginFailed = 4;

    /// <summary><c>send message</c>: the recipient rejected the message or never acknowledged it.</summary>
    public const int NotDelivered = 5;
}
