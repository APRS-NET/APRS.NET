namespace AprsNet.AprsIsTcpClient;

/// <summary>The server's answer to a login.</summary>
/// <param name="Callsign">Callsign the server recorded.</param>
/// <param name="Status">Verified or unverified.</param>
/// <param name="Server">Server name, when the server reported one.</param>
public sealed record LoginResult(string Callsign, LoginStatus Status, string? Server)
{
    /// <summary>Parses a <c># logresp CALL verified, server NAME</c> line.</summary>
    internal static LoginResult? TryParse(string line)
    {
        const string prefix = "# logresp";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = line[prefix.Length..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        LoginStatus status;
        if (parts[1].Equals("verified", StringComparison.OrdinalIgnoreCase))
        {
            status = LoginStatus.Verified;
        }
        else if (parts[1].Equals("unverified", StringComparison.OrdinalIgnoreCase))
        {
            status = LoginStatus.Unverified;
        }
        else
        {
            return null;
        }

        var serverIndex = Array.FindIndex(parts, p => p.Equals("server", StringComparison.OrdinalIgnoreCase));
        var server = serverIndex >= 0 && serverIndex + 1 < parts.Length ? parts[serverIndex + 1] : null;
        return new LoginResult(parts[0], status, server);
    }
}
