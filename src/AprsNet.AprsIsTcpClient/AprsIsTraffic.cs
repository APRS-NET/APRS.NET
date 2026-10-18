using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient;

/// <summary>Which way an <see cref="AprsIsTrafficEventArgs"/> line went.</summary>
public enum AprsIsTrafficDirection
{
    /// <summary>From the server.</summary>
    Received,

    /// <summary>To the server.</summary>
    Sent,
}

/// <summary>
/// What an <see cref="AprsIsTrafficEventArgs"/> line is. A stable vocabulary: members may be added but are never renamed
/// or reused. <see cref="AprsIsTrafficKindExtensions.Describe"/> gives a sentence for people.
/// </summary>
public enum AprsIsTrafficKind
{
    /// <summary>The login line sent, with the passcode masked (<c>pass *****</c>; a read-only <c>-1</c> is shown).</summary>
    LoginSent,

    /// <summary>The server's answer to the login (<c># logresp …</c>).</summary>
    LoginResponse,

    /// <summary>Any other line from the server starting with <c>#</c>: its banner, keepalives, notices.</summary>
    ServerComment,

    /// <summary>A packet from the server; <see cref="AprsIsTrafficEventArgs.Packet"/> holds it parsed.</summary>
    Packet,

    /// <summary>A <c>#filter</c> command sent to change the server-side filter.</summary>
    FilterSent,

    /// <summary>A packet sent; <see cref="AprsIsTrafficEventArgs.Packet"/> holds it.</summary>
    PacketSent,

    /// <summary>A server command sent by hand (<see cref="AprsIsConnection.SendCommandAsync"/>).</summary>
    CommandSent,
}

/// <summary>Text for <see cref="AprsIsTrafficKind"/> values.</summary>
public static class AprsIsTrafficKindExtensions
{
    /// <summary>A short sentence describing the kind, for a console or a log.</summary>
    public static string Describe(this AprsIsTrafficKind kind) =>
        kind switch
        {
            AprsIsTrafficKind.LoginSent => "The login sent, passcode masked.",
            AprsIsTrafficKind.LoginResponse => "The server's answer to the login.",
            AprsIsTrafficKind.ServerComment => "A comment from the server.",
            AprsIsTrafficKind.Packet => "A packet from the server.",
            AprsIsTrafficKind.FilterSent => "A filter change sent.",
            AprsIsTrafficKind.PacketSent => "A packet sent.",
            AprsIsTrafficKind.CommandSent => "A server command sent by hand.",
            _ => kind.ToString(),
        };
}

/// <summary>
/// One line of an APRS-IS connection's traffic, for a troubleshooting console. See
/// <see cref="AprsIsConnection.Traffic"/>.
/// </summary>
public sealed class AprsIsTrafficEventArgs : EventArgs
{
    internal AprsIsTrafficEventArgs(DateTimeOffset at, AprsIsTrafficKind kind, string line)
    {
        At = at;
        Kind = kind;
        Line = line;
    }

    /// <summary>When it happened.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Which way it went.</summary>
    public AprsIsTrafficDirection Direction =>
        Kind is AprsIsTrafficKind.LoginResponse or AprsIsTrafficKind.ServerComment or AprsIsTrafficKind.Packet
            ? AprsIsTrafficDirection.Received
            : AprsIsTrafficDirection.Sent;

    /// <summary>What it is.</summary>
    public AprsIsTrafficKind Kind { get; }

    /// <summary>
    /// The line, without its line ending, as Latin-1 text (one character per byte). For
    /// <see cref="AprsIsTrafficKind.LoginSent"/> the passcode is masked.
    /// </summary>
    public string Line { get; }

    /// <summary>For a packet received or sent, the packet.</summary>
    public AprsPacket? Packet { get; init; }
}
