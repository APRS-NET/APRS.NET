namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// The other end answered the login with KISS frames: it is a KISS TNC, such as Dire Wolf's KISS port (8001), not an
/// APRS-IS server. Trying again won't help; connect to it as a TNC with <c>AprsNet.Kiss</c> instead.
/// </summary>
public sealed class KissTncLoginException : LoginException
{
    /// <summary>Creates the exception with the standard message.</summary>
    public KissTncLoginException()
        : base("The other end sent KISS frames: it is a KISS TNC, not an APRS-IS server. Connect to it as a TNC.") { }
}
