namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// The server refused the login with a comment such as <c># Login by user not allowed</c> instead of a
/// <c># logresp</c>. Some servers refuse well-known placeholder callsigns like <c>N0CALL</c>; others accept them as unverified.
/// </summary>
public sealed class LoginRejectedException : LoginException
{
    private const string _notAllowed = "# Login by user not allowed";

    /// <summary>Creates the exception from the server's message, without the leading <c>#</c>.</summary>
    // ReSharper disable once MemberCanBePrivate.Global (public API: callers can create it, for example in tests)
    public LoginRejectedException(string serverMessage)
        : base($"The server rejected the login: {serverMessage}")
    {
        ServerMessage = serverMessage;
    }

    /// <summary>What the server said, for example <c>Login by user not allowed</c>.</summary>
    public string ServerMessage { get; }

    /// <summary>True for a server comment that refuses the login.</summary>
    internal static bool IsRejection(string line) => line.StartsWith(_notAllowed, StringComparison.OrdinalIgnoreCase);

    internal static LoginRejectedException FromLine(string line) => new(line.TrimStart('#', ' '));
}
