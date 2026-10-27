using System.Diagnostics.CodeAnalysis;
using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>What kind of group traffic an <see cref="AnsrvrMessage"/> is.</summary>
public enum AnsrvrMessageKind
{
    /// <summary>A member's post, relayed to this station by ANSRVR in the member's name.</summary>
    Relay,

    /// <summary>ANSRVR's own reply about a group, such as <c>N:NZEVENT 2 Messages Sent</c>.</summary>
    Notice,
}

/// <summary>
/// A message from a group on ANSRVR, the APRS announcement server: a member's post relayed to this station, or
/// ANSRVR's notice about a group. Both read <c>N:GROUP text</c>; <see cref="TryParse(IncomingMessage, out AnsrvrMessage?)"/>
/// tells them apart by who sent them.
/// </summary>
/// <remarks>
/// <para>
/// A relay comes from the member who posted, not from ANSRVR: ANSRVR's host sends it in their name (on APRS-IS with
/// the destination <c>APWW11</c> and <c>qAO</c>), with the poster's <c>CQ GROUP</c> replaced by <c>N:GROUP</c> and no
/// message ID, so it isn't acked. For example, when N1KXJ-14 sends <c>CQ NEDR Pretty quiet</c>, each other member gets
/// <c>N1KXJ-14&gt;APWW11,qAO,KJ4ERJ-15::N1KXJ-10 :N:NEDR Pretty quiet</c>.
/// </para>
/// <para>
/// A notice comes from <see cref="AnsrvrCommand.Ansrvr"/> with a message ID, and is acked like any message: the answer
/// to a join (<c>N:C4FM Now Monitoring with 2 Members</c>), a post (<c>N:NEDR 2 Messages Sent</c>) or a leave
/// (<c>N:HOTG Removed From Group</c>). ANSRVR's replies that name no group, such as <c>No Memberships</c>, aren't
/// recognised.
/// </para>
/// <para>
/// A message with a message ID from any other station is never a relay, even if it starts with <c>N:</c>: a person
/// typed it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// station.MessageReceived += (_, e) =>
/// {
///     if (AnsrvrMessage.TryParse(e.Message, out var group))
///     {
///         ShowInGroup(group.Group, group.From, group.Text);
///     }
/// };
/// </code>
/// </example>
public sealed class AnsrvrMessage
{
    private const string Prefix = "N:";

    private AnsrvrMessage(AnsrvrMessageKind kind, string group, Callsign from, string text)
    {
        Kind = kind;
        Group = group;
        From = from;
        Text = text;
    }

    /// <summary>A member's post, or ANSRVR's notice.</summary>
    public AnsrvrMessageKind Kind { get; }

    /// <summary>The group, as written, such as <c>NEDR</c>.</summary>
    public string Group { get; }

    /// <summary>
    /// For a <see cref="AnsrvrMessageKind.Relay"/>, the member who posted; for a notice, <see cref="AnsrvrCommand.Ansrvr"/>.
    /// </summary>
    public Callsign From { get; }

    /// <summary>The text after the group, such as <c>Pretty quiet</c> or <c>2 Messages Sent</c>; empty when there is none.</summary>
    public string Text { get; }

    /// <summary>Recognises a message <see cref="MessagingStation.MessageReceived"/> gave.</summary>
    /// <returns><see langword="false"/> if <paramref name="message"/> isn't a relay or notice from a group.</returns>
    public static bool TryParse(IncomingMessage message, [NotNullWhen(true)] out AnsrvrMessage? ansrvr)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TryParse(message.From, message.Text, message.MessageId, out ansrvr);
    }

    /// <summary>Recognises a message from <paramref name="from"/> with <paramref name="text"/> and its message ID.</summary>
    /// <param name="from">The station it came from.</param>
    /// <param name="text">The message text, without the message ID.</param>
    /// <param name="messageId">Its message ID, or <see langword="null"/> when it has none.</param>
    /// <param name="ansrvr">The relay or notice.</param>
    /// <returns><see langword="false"/> if it isn't a relay or notice from a group.</returns>
    public static bool TryParse(
        Callsign from,
        string? text,
        string? messageId,
        [NotNullWhen(true)] out AnsrvrMessage? ansrvr
    )
    {
        ArgumentNullException.ThrowIfNull(from);
        ansrvr = null;
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text[Prefix.Length..];
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        var group = space < 0 ? rest : rest[..space];
        if (group.Length == 0 || group.Any(char.IsControl))
        {
            return false;
        }

        var fromAnsrvr = string.Equals(from.Value, AnsrvrCommand.Ansrvr.Value, StringComparison.OrdinalIgnoreCase);
        if (!fromAnsrvr && messageId is not null)
        {
            return false;
        }

        ansrvr = new AnsrvrMessage(
            fromAnsrvr ? AnsrvrMessageKind.Notice : AnsrvrMessageKind.Relay,
            group,
            from,
            space < 0 ? string.Empty : rest[(space + 1)..]
        );
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{From.Value} {Prefix}{Group} {Text}".TrimEnd();
}
