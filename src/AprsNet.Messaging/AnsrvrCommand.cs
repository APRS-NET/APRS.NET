using System.Diagnostics.CodeAnalysis;
using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>What an <see cref="AnsrvrCommand"/> asks ANSRVR to do.</summary>
public enum AnsrvrCommandKind
{
    /// <summary>Join a group quietly: <c>J GROUP</c>.</summary>
    Join,

    /// <summary>Leave a group: <c>U GROUP</c>.</summary>
    Leave,

    /// <summary>Send text to every member of a group, joining it too: <c>CQ GROUP text</c>.</summary>
    Post,

    /// <summary>Stay a member of one or more groups, quietly: <c>K GROUP1 GROUP2</c>.</summary>
    KeepAlive,
}

/// <summary>
/// A message to ANSRVR, the APRS announcement server: join, leave, post to or keep alive a group. Send its
/// <see cref="Text"/> to <see cref="Ansrvr"/> with <see cref="MessagingStation.Send"/>; ANSRVR acks it like any message.
/// </summary>
/// <remarks>
/// ANSRVR forgets a member it hasn't heard from in 12 hours (<see cref="MembershipLifetime"/>), so an app keeps a joined
/// group with a <see cref="KeepAlive"/> more often than that, every 8 hours as APRSIS32 does
/// (<see cref="KeepAliveInterval"/>). A post keeps its group alive too.
/// </remarks>
/// <example>
/// <code>
/// station.Send(AnsrvrCommand.Ansrvr, AnsrvrCommand.Join("NZEVENT").Text);
/// station.Send(AnsrvrCommand.Ansrvr, AnsrvrCommand.Post("NZEVENT", "Net at 7pm on 146.52").Text);
/// </code>
/// </example>
public sealed class AnsrvrCommand : IEquatable<AnsrvrCommand>
{
    private const int MaxText = 67;

    private AnsrvrCommand(AnsrvrCommandKind kind, string[] groups, string? post)
    {
        Kind = kind;
        Groups = groups;
        Message = post;
        Text = kind switch
        {
            AnsrvrCommandKind.Join => $"J {groups[0]}",
            AnsrvrCommandKind.Leave => $"U {groups[0]}",
            AnsrvrCommandKind.Post => $"CQ {groups[0]} {post}",
            _ => $"K {string.Join(' ', groups)}",
        };
        if (Text.Length > MaxText)
        {
            throw new ArgumentException(
                $"An ANSRVR message is at most {MaxText} characters, like any message; this one is {Text.Length}."
            );
        }
    }

    /// <summary>The announcement server's callsign, <c>ANSRVR</c>: send every command to it.</summary>
    public static Callsign Ansrvr { get; } = Callsign.Parse("ANSRVR");

    /// <summary>How long ANSRVR keeps a member it hasn't heard from: 12 hours.</summary>
    public static TimeSpan MembershipLifetime { get; } = TimeSpan.FromHours(12);

    /// <summary>How often to send a <see cref="KeepAlive"/> for a joined group: every 8 hours, inside the 12.</summary>
    public static TimeSpan KeepAliveInterval { get; } = TimeSpan.FromHours(8);

    /// <summary>What it asks ANSRVR to do.</summary>
    public AnsrvrCommandKind Kind { get; }

    /// <summary>The group, or for a keep-alive the groups, as given.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>For a <see cref="AnsrvrCommandKind.Post"/>, the text sent to the group; otherwise <see langword="null"/>.</summary>
    public string? Message { get; }

    /// <summary>The message text to send to ANSRVR, such as <c>J NZEVENT</c>.</summary>
    public string Text { get; }

    /// <summary>Join <paramref name="group"/> quietly.</summary>
    /// <param name="group">The group name, such as <c>NZEVENT</c>: no spaces.</param>
    /// <exception cref="ArgumentException">The group name can't be sent.</exception>
    public static AnsrvrCommand Join(string group) => new(AnsrvrCommandKind.Join, [CheckGroup(group)], null);

    /// <summary>Leave <paramref name="group"/>.</summary>
    /// <param name="group">The group name: no spaces.</param>
    /// <exception cref="ArgumentException">The group name can't be sent.</exception>
    public static AnsrvrCommand Leave(string group) => new(AnsrvrCommandKind.Leave, [CheckGroup(group)], null);

    /// <summary>
    /// Send <paramref name="text"/> to every member of <paramref name="group"/>; it joins the group too, and creates it
    /// if no one has.
    /// </summary>
    /// <param name="group">The group name: no spaces.</param>
    /// <param name="text">The text; with the group, at most 67 characters in all.</param>
    /// <exception cref="ArgumentException">The group name or text can't be sent, or together they are too long.</exception>
    public static AnsrvrCommand Post(string group, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        CheckCharacters(text, nameof(text));
        return new(AnsrvrCommandKind.Post, [CheckGroup(group)], text);
    }

    /// <summary>Stay a member of <paramref name="groups"/>, quietly.</summary>
    /// <param name="groups">One or more group names: no spaces; with the command, at most 67 characters in all.</param>
    /// <exception cref="ArgumentException">There are no groups, a name can't be sent, or they are too long together.</exception>
    public static AnsrvrCommand KeepAlive(params IEnumerable<string> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        string[] names = [.. groups.Select(CheckGroup)];
        if (names.Length == 0)
        {
            throw new ArgumentException("A keep-alive names at least one group.", nameof(groups));
        }

        return new(AnsrvrCommandKind.KeepAlive, names, null);
    }

    /// <summary>
    /// Reads a message to ANSRVR, such as one an app sent and shows in a conversation. The command letters may be in
    /// either case; group names are kept as written.
    /// </summary>
    /// <returns><see langword="false"/> if <paramref name="text"/> isn't a join, leave, post or keep-alive.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AnsrvrCommand? command)
    {
        command = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxText || HasBadCharacters(text))
        {
            return false;
        }

        var parts = text.Split(' ', 3);
        var kind = parts[0].ToUpperInvariant() switch
        {
            "J" => AnsrvrCommandKind.Join,
            "U" => AnsrvrCommandKind.Leave,
            "CQ" => AnsrvrCommandKind.Post,
            "K" => AnsrvrCommandKind.KeepAlive,
            _ => (AnsrvrCommandKind?)null,
        };
        if (kind is null || parts.Length < 2 || parts[1].Length == 0)
        {
            return false;
        }

        switch (kind)
        {
            case AnsrvrCommandKind.Post when parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]):
                command = new(AnsrvrCommandKind.Post, [parts[1]], parts[2]);
                return true;
            case AnsrvrCommandKind.KeepAlive:
                var groups = text[(parts[0].Length + 1)..].Split(' ');
                if (groups.Any(g => g.Length == 0))
                {
                    return false;
                }

                command = new(AnsrvrCommandKind.KeepAlive, groups, null);
                return true;
            case AnsrvrCommandKind.Join or AnsrvrCommandKind.Leave when parts.Length == 2:
                command = new(kind.Value, [parts[1]], null);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public bool Equals(AnsrvrCommand? other) => other is not null && Text == other.Text;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as AnsrvrCommand);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    /// <inheritdoc />
    public override string ToString() => Text;

    private static string CheckGroup(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        if (group.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("An ANSRVR group name has no spaces.", nameof(group));
        }

        CheckCharacters(group, nameof(group));
        return group;
    }

    private static void CheckCharacters(string value, string name)
    {
        if (HasBadCharacters(value))
        {
            throw new ArgumentException("Message text must not contain '|', '~', '{' or control characters.", name);
        }
    }

    private static bool HasBadCharacters(string value) =>
        value.AsSpan().IndexOfAny("|~{") >= 0 || value.Any(char.IsControl);
}
