using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AprsNet.AprsIsTcpClient;

/// <summary>A host and port to connect to. Presets cover the aprs2.net rotates; any other server can be built directly.</summary>
public sealed record AprsIsEndpoint
{
    /// <summary>Worldwide Tier 2 rotate on the user-defined filter port.</summary>
    public static AprsIsEndpoint Rotate { get; } = new("rotate.aprs2.net");

    /// <summary>North American Tier 2 rotate.</summary>
    public static AprsIsEndpoint NorthAmerica { get; } = new("noam.aprs2.net");

    /// <summary>South American Tier 2 rotate.</summary>
    public static AprsIsEndpoint SouthAmerica { get; } = new("soam.aprs2.net");

    /// <summary>European and African Tier 2 rotate.</summary>
    public static AprsIsEndpoint Europe { get; } = new("euro.aprs2.net");

    /// <summary>Asian Tier 2 rotate.</summary>
    public static AprsIsEndpoint Asia { get; } = new("asia.aprs2.net");

    /// <summary>Oceanian Tier 2 rotate.</summary>
    public static AprsIsEndpoint Oceania { get; } = new("aunz.aprs2.net");

    /// <summary>Creates an endpoint, validating the host and port.</summary>
    /// <param name="host">DNS name or IP address.</param>
    /// <param name="port">TCP port, 1 to 65535.</param>
    public AprsIsEndpoint(string host, int port = AprsIsPorts.UserDefinedFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Host = host.Trim();
        Port = port;
    }

    /// <summary>DNS name or IP address.</summary>
    public string Host { get; }

    /// <summary>TCP port.</summary>
    public int Port { get; }

    /// <summary>Returns a copy of this endpoint with a different port.</summary>
    public AprsIsEndpoint WithPort(int port) => new(Host, port);

    /// <summary>Parses <c>host</c>, <c>host:port</c> or <c>[ipv6]:port</c>. A missing port means the user-defined filter port.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AprsIsEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        string host;
        string? portText = null;

        if (text[0] == '[')
        {
            var close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = text[1..close];
            var tail = text[(close + 1)..];
            if (tail.Length > 0)
            {
                if (tail[0] != ':')
                {
                    return false;
                }

                portText = tail[1..];
            }
        }
        else
        {
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon != text.LastIndexOf(':'))
            {
                return false; // bare IPv6 literals need brackets
            }

            host = colon < 0 ? text : text[..colon];
            portText = colon < 0 ? null : text[(colon + 1)..];
        }

        var port = AprsIsPorts.UserDefinedFilter;
        if (
            portText is not null
            && !(
                int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && port is >= 1 and <= 65535
            )
        )
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        endpoint = new AprsIsEndpoint(host, port);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() =>
        Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
