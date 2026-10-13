using System.Globalization;
using System.Text;

namespace AprsNet.Packets;

/// <summary>Decodes messages and telemetry definitions (APRS101 ch. 13–15, APRS 1.1 reply-acks).</summary>
internal static class MessageDecoder
{
    /// <summary>Decodes a message; <paramref name="offset"/> is where the information field starts in Raw.</summary>
    public static AprsPayload? Decode(ReadOnlyMemory<byte> information, int offset, List<ParseIssue> issues)
    {
        // :ADDRESSEE:text — a 9-character addressee padded with spaces, then a colon.
        var info = information.Span;
        if (!TryAddressee(info, out var colon))
        {
            issues.Add(new ParseIssue(ParseIssueCode.InvalidAddressee, ParseIssueSeverity.Error, offset + 1));
            return null;
        }

        if (colon != 10)
        {
            issues.Add(new ParseIssue(ParseIssueCode.UnpaddedAddressee, ParseIssueSeverity.Warning, offset + 1));
        }

        var addressee = Encoding.Latin1.GetString(info[1..colon]).TrimEnd(' ');
        var text = information[(colon + 1)..];
        if (TelemetryKind(text.Span) is { } kind)
        {
            return Telemetry(addressee, kind, text[5..], offset + colon + 6, issues);
        }

        if (TryAck(text.Span, out var ackKind, out var ackId, out var ackReply))
        {
            return new MessagePayload
            {
                Addressee = addressee,
                Kind = ackKind,
                MessageId = ackId,
                ReplyAck = ackReply,
            };
        }

        var (body, id, replyAck) = SplitMessageId(text);
        var bodyText = AprsText.Decode(body.Span);
        var message = new MessagePayload
        {
            Addressee = addressee,
            Text = bodyText,
            TextBytes = body,
            MessageId = id,
            ReplyAck = replyAck,
        };

        if (addressee.StartsWith("BLN", StringComparison.Ordinal) && addressee.Length > 3)
        {
            var bulletin = addressee[3..];
            // APRS101 defines BLNA–BLNZ; senders also use longer names (BLNALUX, BLNA-WEB), read as a group.
            if (char.IsAsciiLetterUpper(bulletin[0]))
            {
                return Copy(
                    message,
                    MessageKind.Announcement,
                    bulletinId: bulletin[..1],
                    group: bulletin.Length > 1 ? bulletin[1..] : null
                );
            }

            if (char.IsAsciiDigit(bulletin[0]))
            {
                return Copy(
                    message,
                    MessageKind.Bulletin,
                    bulletinId: bulletin[..1],
                    group: bulletin.Length > 1 ? bulletin[1..] : null
                );
            }
        }

        if (addressee.StartsWith("NWS-", StringComparison.Ordinal))
        {
            return Copy(message, MessageKind.NwsBulletin);
        }

        if (bodyText.StartsWith('?'))
        {
            var query = bodyText[1..].TrimEnd();
            var end = query.IndexOfAny(['?', ' ']);
            var type = end < 0 ? query : query[..end];
            var argument = end < 0 ? "" : query[(end + 1)..].Trim(' ', '?');
            return Copy(
                message,
                MessageKind.Query,
                queryType: type,
                queryArgument: argument.Length > 0 ? argument : null
            );
        }

        return message;
    }

    private static MessagePayload Copy(
        MessagePayload message,
        MessageKind kind,
        string? bulletinId = null,
        string? group = null,
        string? queryType = null,
        string? queryArgument = null
    ) =>
        new()
        {
            Addressee = message.Addressee,
            Kind = kind,
            Text = message.Text,
            TextBytes = message.TextBytes,
            MessageId = message.MessageId,
            ReplyAck = message.ReplyAck,
            BulletinId = bulletinId,
            BulletinGroup = group,
            QueryType = queryType,
            QueryArgument = queryArgument,
        };

    /// <summary>
    /// Splits a trailing <c>{id</c> (up to 5 characters) or APRS 1.1 <c>{MM}AA</c> off the text. A <c>{</c> that is not
    /// followed by a valid ID stays in the text.
    /// </summary>
    private static (ReadOnlyMemory<byte> Text, string? Id, string? ReplyAck) SplitMessageId(ReadOnlyMemory<byte> text)
    {
        var span = text.Span;
        var brace = span.LastIndexOf((byte)'{');
        if (brace < 0 || !TryId(span[(brace + 1)..], out var id, out var replyAck))
        {
            return (text, null, null);
        }

        return (text[..brace], id, replyAck);
    }

    /// <summary><c>ack</c> or <c>rej</c> followed by the message ID it answers, and nothing else.</summary>
    private static bool TryAck(ReadOnlySpan<byte> text, out MessageKind kind, out string? id, out string? replyAck)
    {
        kind = text.StartsWith("rej"u8) ? MessageKind.Rejection : MessageKind.Acknowledgement;
        (id, replyAck) = (null, null);
        return (text.StartsWith("ack"u8) || text.StartsWith("rej"u8)) && TryId(text[3..], out id, out replyAck);
    }

    /// <summary>A message ID of 1–5 printable characters, optionally followed by <c>}</c> and a reply-ack of 0–5.</summary>
    private static bool TryId(ReadOnlySpan<byte> field, out string? id, out string? replyAck)
    {
        (id, replyAck) = (null, null);
        var close = field.IndexOf((byte)'}');
        var main = close < 0 ? field : field[..close];
        var reply = close < 0 ? default : field[(close + 1)..];
        if (main.Length is < 1 or > 5 || reply.Length > 5 || !IdCharacters(main) || !IdCharacters(reply))
        {
            return false;
        }

        id = Encoding.ASCII.GetString(main);
        replyAck = close < 0 ? null : Encoding.ASCII.GetString(reply);
        return true;

        static bool IdCharacters(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExceptInRange((byte)'!', (byte)'z') < 0;
    }

    /// <summary>
    /// Finds the colon after the addressee. APRS101 pads the addressee to 9 characters, but some senders do not pad it
    /// (SvxLink's <c>:EL-N0CALL:</c>) or pad it past 9 (<c>:BLNRAIN    :</c>), so a 1–9 character callsign-like name
    /// followed by spaces and a colon within the first 12 bytes is accepted too.
    /// </summary>
    private static bool TryAddressee(ReadOnlySpan<byte> info, out int colon)
    {
        var found = info.Length > 1 ? info[1..Math.Min(info.Length, 13)].IndexOf((byte)':') : -1;
        colon = info.Length > 10 && info[10] == ':' ? 10 : found + 1;
        if (colon == 10)
        {
            return true;
        }

        if (colon < 2)
        {
            return false;
        }

        var name = info[1..colon].TrimEnd((byte)' ');
        return name.Length is >= 1 and <= 9 && name.IndexOfAnyExcept(_addresseeCharacters) < 0;
    }

    private static readonly System.Buffers.SearchValues<byte> _addresseeCharacters = System.Buffers.SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-"u8
    );

    private static TelemetryDefinitionKind? TelemetryKind(ReadOnlySpan<byte> text) =>
        text.StartsWith("PARM."u8) ? TelemetryDefinitionKind.ParameterNames
        : text.StartsWith("UNIT."u8) ? TelemetryDefinitionKind.Units
        : text.StartsWith("EQNS."u8) ? TelemetryDefinitionKind.Equations
        : text.StartsWith("BITS."u8) ? TelemetryDefinitionKind.BitSense
        : null;

    private static TelemetryDefinition Telemetry(
        string addressee,
        TelemetryDefinitionKind kind,
        ReadOnlyMemory<byte> body,
        int offset,
        List<ParseIssue> issues
    )
    {
        var text = AprsText.Decode(body.Span).TrimEnd();
        switch (kind)
        {
            case TelemetryDefinitionKind.Equations:
                var numbers = text.Split(',');
                var equations = new List<TelemetryEquation>();
                if (numbers.Length % 3 == 0 && numbers.Length <= 15)
                {
                    for (var i = 0; i < numbers.Length; i += 3)
                    {
                        if (
                            Number(numbers[i]) is not { } a
                            || Number(numbers[i + 1]) is not { } b
                            || Number(numbers[i + 2]) is not { } c
                        )
                        {
                            equations.Clear();
                            break;
                        }

                        equations.Add(new TelemetryEquation(a, b, c));
                    }
                }

                if (equations.Count == 0)
                {
                    issues.Add(
                        new ParseIssue(ParseIssueCode.InvalidTelemetryDefinition, ParseIssueSeverity.Warning, offset)
                    );
                }

                return new TelemetryDefinition
                {
                    Addressee = addressee,
                    Kind = kind,
                    Equations = equations,
                };

            case TelemetryDefinitionKind.BitSense:
                var comma = text.IndexOf(',', StringComparison.Ordinal);
                var bits = comma < 0 ? text : text[..comma];
                if (bits.Length != 8 || bits.AsSpan().IndexOfAnyExcept('0', '1') >= 0)
                {
                    issues.Add(
                        new ParseIssue(ParseIssueCode.InvalidTelemetryDefinition, ParseIssueSeverity.Warning, offset)
                    );
                    return new TelemetryDefinition { Addressee = addressee, Kind = kind };
                }

                return new TelemetryDefinition
                {
                    Addressee = addressee,
                    Kind = kind,
                    BitSense = bits,
                    ProjectName = comma < 0 ? null : text[(comma + 1)..],
                };

            default:
                return new TelemetryDefinition
                {
                    Addressee = addressee,
                    Kind = kind,
                    Labels = text.Split(','),
                };
        }

        static double? Number(string field) =>
            double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
