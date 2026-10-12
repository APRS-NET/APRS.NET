namespace AprsNet.AprsIsTcpClient;

/// <summary>Whether the server accepted the passcode.</summary>
public enum LoginStatus
{
    /// <summary>The passcode matched. The login can transmit.</summary>
    Verified,

    /// <summary>The passcode was wrong or <c>-1</c>. The login is receive-only.</summary>
    Unverified,
}
