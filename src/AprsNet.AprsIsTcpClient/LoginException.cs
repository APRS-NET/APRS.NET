namespace AprsNet.AprsIsTcpClient;

/// <summary>The login did not complete: the connection closed, timed out, or the response was unreadable.</summary>
public class LoginException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public LoginException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and cause.</summary>
    public LoginException(string message, Exception innerException)
        : base(message, innerException) { }
}
