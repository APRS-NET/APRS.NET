using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AprsNet.Packets;

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
    /// <exception cref="AprsValidationException">
    /// The host is empty (<see cref="AprsValidationErrorCode.ServerHostEmpty"/>) or the port is outside 1 to 65535
    /// (<see cref="AprsValidationErrorCode.ServerPortOutOfRange"/>).
    /// </exception>
    public AprsIsEndpoint(string host, int port = AprsIsPorts.UserDefinedFilter)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(host))
        {
            throw Error(AprsValidationErrorCode.ServerHostEmpty, nameof(host), host);
        }

        if (port is < 1 or > 65535)
        {
            throw Error(
                AprsValidationErrorCode.ServerPortOutOfRange,
                nameof(port),
                port.ToString(CultureInfo.InvariantCulture)
            );
        }

        Host = host.Trim();
        Port = port;
    }

    /// <summary>DNS name or IP address.</summary>
    public string Host { get; }

    /// <summary>TCP port.</summary>
    public int Port { get; }

    /// <summary>Returns a copy of this endpoint with a different port.</summary>
    public AprsIsEndpoint WithPort(int port) => new(Host, port);

    /// <summary>
    /// Parses <c>host</c>, <c>host:port</c> or <c>[ipv6]:port</c>, as a person types a server address. A missing port
    /// means the user-defined filter port.
    /// </summary>
    /// <exception cref="AprsValidationException">
    /// The text is not a server address; <see cref="AprsValidationException.Code"/> says why (one of the
    /// <c>Server…</c> codes).
    /// </exception>
    public static AprsIsEndpoint Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text, out var endpoint, out var code, out var index)
            ? endpoint
            : throw Error(code, nameof(text), text, index);
    }

    /// <summary>Parses <c>host</c>, <c>host:port</c> or <c>[ipv6]:port</c>. A missing port means the user-defined filter port.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AprsIsEndpoint? endpoint) =>
        Parse(text, out endpoint, out _, out _);

    private static bool Parse(
        string? text,
        [NotNullWhen(true)] out AprsIsEndpoint? endpoint,
        out AprsValidationErrorCode code,
        out int? index
    )
    {
        endpoint = null;
        index = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            code = AprsValidationErrorCode.ServerHostEmpty;
            return false;
        }

        var trimmed = text.Trim();
        var start = text.IndexOf(trimmed, StringComparison.Ordinal); // indexes point into the text as given
        string host;
        string? portText = null;
        var portStart = 0;

        if (trimmed[0] == '[')
        {
            var close = trimmed.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                code = AprsValidationErrorCode.ServerHostInvalid;
                index = start;
                return false;
            }

            host = trimmed[1..close];
            var tail = trimmed[(close + 1)..];
            if (tail.Length > 0)
            {
                if (tail[0] != ':')
                {
                    code = AprsValidationErrorCode.ServerHostInvalid;
                    index = start + close + 1;
                    return false;
                }

                portText = tail[1..];
                portStart = close + 2;
            }
        }
        else
        {
            var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (colon != trimmed.LastIndexOf(':'))
            {
                code = AprsValidationErrorCode.ServerHostInvalid; // bare IPv6 literals need brackets
                index = start + trimmed.LastIndexOf(':');
                return false;
            }

            host = colon < 0 ? trimmed : trimmed[..colon];
            portText = colon < 0 ? null : trimmed[(colon + 1)..];
            portStart = colon + 1;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            code = AprsValidationErrorCode.ServerHostEmpty;
            return false;
        }

        var space = host.AsSpan().IndexOfAny(" \t");
        if (space >= 0)
        {
            code = AprsValidationErrorCode.ServerHostInvalid;
            index = start + (trimmed[0] == '[' ? 1 : 0) + space;
            return false;
        }

        var port = AprsIsPorts.UserDefinedFilter;
        if (portText is not null)
        {
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                var bad = portText.AsSpan().IndexOfAnyExceptInRange('0', '9');
                code =
                    portText.Length > 0 && bad < 0
                        ? AprsValidationErrorCode.ServerPortOutOfRange // all digits, too many of them
                        : AprsValidationErrorCode.ServerPortInvalid;
                index = bad < 0 ? null : start + portStart + bad;
                return false;
            }

            if (port is < 1 or > 65535)
            {
                code = AprsValidationErrorCode.ServerPortOutOfRange;
                return false;
            }
        }

        code = default;
        endpoint = new AprsIsEndpoint(host, port);
        return true;
    }

    private static AprsValidationException Error(
        AprsValidationErrorCode code,
        string paramName,
        string value,
        int? index = null
    ) =>
        new(
            code,
            code switch
            {
                AprsValidationErrorCode.ServerHostEmpty => "The server address has no host.",
                AprsValidationErrorCode.ServerPortInvalid => "The server port must be a whole number.",
                AprsValidationErrorCode.ServerPortOutOfRange => "The server port must be from 1 to 65535.",
                _ => "The server address must be host, host:port or [ipv6]:port.",
            },
            paramName,
            value,
            index
        );

    /// <inheritdoc />
    public override string ToString() =>
        Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
