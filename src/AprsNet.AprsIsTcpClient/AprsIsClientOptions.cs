using System.Reflection;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Settings for <see cref="AprsIsClient"/>.</summary>
public sealed class AprsIsClientOptions
{
    /// <summary>Server to connect to. Defaults to the worldwide Tier 2 rotate on port 14580.</summary>
    public AprsIsEndpoint Endpoint { get; init; } = AprsIsEndpoint.Rotate;

    /// <summary>Login callsign, with optional SSID (for example <c>N0CALL-10</c>). Required.</summary>
    public string Callsign { get; set; } = "";

    /// <summary>APRS-IS passcode. <c>-1</c> logs in read-only. Never logged.</summary>
    public int Passcode { get; set; } = -1;

    /// <summary>Server-side filter sent with the login, for example <c>r/35/-97/200</c>. Null sends none.</summary>
    public string? Filter { get; set; }

    /// <summary>Software name reported to the server. No whitespace.</summary>
    public string SoftwareName { get; init; } = "AprsNet";

    /// <summary>Software version reported to the server. No whitespace.</summary>
    public string SoftwareVersion { get; init; } = DefaultVersion();

    /// <summary>How long to wait for the TCP connection.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for the server's login response.</summary>
    public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the server may stay silent before the connection is treated as dead. APRS-IS servers send a
    /// <c>#</c> keepalive about every 20 seconds, so silence much longer than that means a stalled link that TCP
    /// alone may not notice for hours. <see cref="Timeout.InfiniteTimeSpan"/> disables the check.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Clock for the login and idle timeouts. Tests can pass a fake provider to control time.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Longest accepted line in bytes, excluding the terminator. Longer lines are discarded.</summary>
    public int MaxLineLength { get; set; } = 2048;

    // ReSharper disable once GrammarMistakeInComment
    /// <summary>Capacity of the bounded receive queue, in lines.</summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>What to do when the receive queue is full.</summary>
    public OverflowPolicy OverflowPolicy { get; set; } = OverflowPolicy.Block;

    /// <summary>True when the passcode is <c>-1</c>, so the login cannot transmit.</summary>
    public bool IsReadOnly => Passcode == -1;

    /// <summary>Throws <see cref="ArgumentException"/> if any setting is unusable.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(TimeProvider);

        if (!IsValidCallsign(Callsign))
        {
            throw new ArgumentException("Callsign must be 1 to 9 letters, digits or '-'.", nameof(Callsign));
        }

        if (!IsToken(SoftwareName) || !IsToken(SoftwareVersion))
        {
            throw new ArgumentException(
                "Software name and version must be non-empty with no whitespace or control characters."
            );
        }

        if (Filter is not null && ContainsLineBreakOrControl(Filter))
        {
            throw new ArgumentException("Filter must not contain control characters.", nameof(Filter));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LoginTimeout, TimeSpan.Zero);
        if (IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleTimeout, TimeSpan.Zero);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLineLength, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(ChannelCapacity, 1);
    }

    internal static bool ContainsLineBreakOrControl(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidCallsign(string value)
    {
        if (value.Length is < 1 or > 9)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string DefaultVersion() =>
        typeof(AprsIsClientOptions)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
