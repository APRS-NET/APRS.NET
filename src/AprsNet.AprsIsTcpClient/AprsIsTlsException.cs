using System.Net.Security;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Why a TLS connection to an APRS-IS server failed; stable values for logs and metrics.</summary>
public enum AprsIsTlsFailure
{
    /// <summary>The server's certificate was not accepted: see <see cref="AprsIsTlsException.PolicyErrors"/>.</summary>
    CertificateRejected,

    /// <summary>The TLS handshake failed for another reason, such as a server that does not speak TLS on that port.</summary>
    HandshakeFailed,
}

/// <summary>
/// A TLS connection to an APRS-IS server failed (<see cref="AprsIsClientOptions.UseTls"/>). It is an
/// <see cref="IOException"/>, so it is handled like any other connection failure, and
/// <see cref="AprsIsConnection"/> retries it.
/// </summary>
public sealed class AprsIsTlsException : IOException
{
    /// <summary>Creates the exception.</summary>
    public AprsIsTlsException(
        AprsIsTlsFailure failure,
        SslPolicyErrors policyErrors,
        string message,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        Failure = failure;
        PolicyErrors = policyErrors;
    }

    /// <summary>Why the connection failed.</summary>
    public AprsIsTlsFailure Failure { get; }

    /// <summary>
    /// For <see cref="AprsIsTlsFailure.CertificateRejected"/>, what was wrong with the server's certificate, such as a
    /// name that does not match the host; <see cref="SslPolicyErrors.None"/> otherwise.
    /// </summary>
    public SslPolicyErrors PolicyErrors { get; }
}
