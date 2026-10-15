using System.Buffers;
using System.Reflection;
using AprsNet.Packets;

// Options are set by callers, which inspectors cannot see from inside this solution.
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

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

    /// <summary>
    /// How many packets <see cref="AprsIsClient.SendAsync"/> sends at once before <see cref="SendInterval"/> applies.
    /// Defaults to 5.
    /// </summary>
    public int SendBurst { get; set; } = 5;

    /// <summary>
    /// After a burst of <see cref="SendBurst"/> packets, how long <see cref="AprsIsClient.SendAsync"/> waits between
    /// packets; a sender waits rather than fails. Defaults to 6 seconds (10 packets a minute), which keeps a runaway
    /// beacon loop from flooding APRS-IS. <see cref="TimeSpan.Zero"/> turns the limit off.
    /// </summary>
    public TimeSpan SendInterval { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// <see cref="AprsIsConnection"/>: how long to wait before the first reconnect. Each failed attempt doubles it, up
    /// to <see cref="MaxReconnectDelay"/>, and every wait varies by up to 20% so many clients do not retry in step.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <see cref="AprsIsConnection"/>: the longest wait between reconnect attempts. A connection that stayed up at
    /// least this long starts again from <see cref="ReconnectDelay"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// <see cref="AprsIsConnection"/>: reconnect when no packet has arrived for this long, even if the server still
    /// sends <c>#</c> keepalives (its feed may have stalled). <see langword="null"/>, the default, turns it off: a
    /// narrow filter can be quiet for hours.
    /// </summary>
    public TimeSpan? MaxPacketSilence { get; set; }

    /// <summary>What to do when the receive queue is full.</summary>
    public OverflowPolicy OverflowPolicy { get; set; } = OverflowPolicy.Block;

    /// <summary>True when the passcode is <c>-1</c>, so the login cannot transmit.</summary>
    public bool IsReadOnly => Passcode == -1;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if any setting is unusable. Settings a person types (callsign, software
    /// name and version, filter) throw <see cref="AprsValidationException"/>, whose
    /// <see cref="AprsValidationException.Code"/> says why.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(TimeProvider);

        if (Callsign is not { Length: >= 1 and <= 9 })
        {
            throw new AprsValidationException(
                AprsValidationErrorCode.CallsignInvalidLength,
                CallsignMessage,
                nameof(Callsign),
                Callsign
            );
        }

        var badCallsign = Callsign.AsSpan().IndexOfAnyExcept(_loginCallsignCharacters);
        if (badCallsign >= 0)
        {
            throw new AprsValidationException(
                AprsValidationErrorCode.CallsignInvalidCharacter,
                CallsignMessage,
                nameof(Callsign),
                Callsign,
                badCallsign
            );
        }

        CheckToken(SoftwareName, nameof(SoftwareName), AprsValidationErrorCode.SoftwareNameInvalid);
        CheckToken(SoftwareVersion, nameof(SoftwareVersion), AprsValidationErrorCode.SoftwareVersionInvalid);
        if (Filter is not null && FilterError(Filter, nameof(Filter)) is { } filterError)
        {
            throw filterError;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LoginTimeout, TimeSpan.Zero);
        if (IdleTimeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(IdleTimeout, TimeSpan.Zero);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLineLength, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(ChannelCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SendBurst, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SendInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay);
        if (MaxPacketSilence is { } silence)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(silence, TimeSpan.Zero, nameof(MaxPacketSilence));
        }
    }

    /// <summary>A copy with another filter, for each connection of an <see cref="AprsIsConnection"/>.</summary>
    internal AprsIsClientOptions WithFilter(string? filter)
    {
        var copy = (AprsIsClientOptions)MemberwiseClone();
        copy.Filter = filter;
        return copy;
    }

    private const string CallsignMessage = "Callsign must be 1 to 9 letters, digits or '-'.";

    internal static readonly SearchValues<char> LettersAndDigits = SearchValues.Create(
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
    );

    private static readonly SearchValues<char> _loginCallsignCharacters = SearchValues.Create(
        "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
    );

    /// <summary>The filter's first control character as an error, or <see langword="null"/> when it has none.</summary>
    internal static AprsValidationException? FilterError(string filter, string paramName)
    {
        var index = IndexOf(filter, char.IsControl);
        return index < 0
            ? null
            : new AprsValidationException(
                AprsValidationErrorCode.FilterControlCharacter,
                "Filter must not contain control characters.",
                paramName,
                filter,
                index
            );
    }

    private static void CheckToken(string value, string paramName, AprsValidationErrorCode code)
    {
        var bad = value is null ? -1 : IndexOf(value, c => char.IsWhiteSpace(c) || char.IsControl(c));
        if (string.IsNullOrEmpty(value) || bad >= 0)
        {
            throw new AprsValidationException(
                code,
                "Software name and version must be non-empty with no whitespace or control characters.",
                paramName,
                value,
                bad >= 0 ? bad : null
            );
        }
    }

    private static int IndexOf(string text, Func<char, bool> match)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (match(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string DefaultVersion() =>
        typeof(AprsIsClientOptions)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
