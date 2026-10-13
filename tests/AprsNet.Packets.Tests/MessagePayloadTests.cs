namespace AprsNet.Packets.Tests;

public class MessagePayloadTests
{
    private static T Decode<T>(string information, out AprsPacket packet)
        where T : AprsPayload
    {
        packet = AprsPacket.Parse("N0CALL>APRS:" + information);
        Assert.Equal(PacketType.Message, packet.Type);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return Assert.IsType<T>(packet.Payload);
    }

    private static MessagePayload Message(string information) => Decode<MessagePayload>(information, out _);

    [Fact]
    public void ReadsAddresseeTextAndId()
    {
        var message = Message(":WU2Z     :Testing{003");

        Assert.Equal("WU2Z", message.Addressee);
        Assert.Equal(MessageKind.Message, message.Kind);
        Assert.Equal("Testing", message.Text);
        Assert.Equal("Testing"u8.ToArray(), message.TextBytes.ToArray());
        Assert.Equal("003", message.MessageId);
        Assert.Null(message.ReplyAck);
        Assert.False(message.IsReplyAckCapable);
        Assert.True(message.IsAckRequired);
    }

    [Fact]
    public void AMessageWithoutAnIdNeedsNoAck()
    {
        var message = Message(":WU2Z     :Testing");

        Assert.Null(message.MessageId);
        Assert.False(message.IsAckRequired);
    }

    [Theory]
    [InlineData(":N0CALL-9 :Hi{12}", "12", "")]
    [InlineData(":N0CALL-9 :Hi{12}AB", "12", "AB")]
    [InlineData(":N0CALL-9 :Hi{ABCDE}VWXYZ", "ABCDE", "VWXYZ")]
    public void ReadsReplyAcks(string information, string id, string replyAck)
    {
        var message = Message(information);

        Assert.Equal("Hi", message.Text);
        Assert.Equal(id, message.MessageId);
        Assert.Equal(replyAck, message.ReplyAck);
        Assert.True(message.IsReplyAckCapable);
    }

    [Theory]
    [InlineData(":N0CALL   :Hi{123456")]
    [InlineData(":N0CALL   :Hi{")]
    [InlineData(":N0CALL   :Hi{1 2")]
    [InlineData(":N0CALL   :Hi{12}ABCDEF")]
    [InlineData(":N0CALL   :Hi{}")]
    public void ABraceWithoutAValidIdStaysInTheText(string information)
    {
        var message = Message(information);

        Assert.Equal(information[11..], message.Text);
        Assert.Null(message.MessageId);
    }

    [Theory]
    [InlineData(":KB2ICI-14:ack003", MessageKind.Acknowledgement, "003", null)]
    [InlineData(":KB2ICI-14:rej003", MessageKind.Rejection, "003", null)]
    [InlineData(":KB2ICI-14:ack12}AB", MessageKind.Acknowledgement, "12", "AB")]
    [InlineData(":KB2ICI-14:rejZ", MessageKind.Rejection, "Z", null)]
    public void ReadsAcksAndRejects(string information, MessageKind kind, string id, string? replyAck)
    {
        var message = Message(information);

        Assert.Equal(kind, message.Kind);
        Assert.Equal(id, message.MessageId);
        Assert.Equal(replyAck, message.ReplyAck);
        Assert.Equal("", message.Text);
        Assert.False(message.IsAckRequired);
    }

    [Theory]
    [InlineData(":N0CALL   :ack")]
    [InlineData(":N0CALL   :acknowledged that")]
    [InlineData(":N0CALL   :reject this idea")]
    public void TextThatOnlyStartsLikeAnAckIsAMessage(string information)
    {
        Assert.Equal(MessageKind.Message, Message(information).Kind);
    }

    [Theory]
    [InlineData(":BLN3     :Snow", MessageKind.Bulletin, "3", null)]
    [InlineData(":BLNQ     :QRT", MessageKind.Announcement, "Q", null)]
    [InlineData(":BLN4WX   :Plows", MessageKind.Bulletin, "4", "WX")]
    [InlineData(":BLN      :Odd", MessageKind.Message, null, null)]
    [InlineData(":BLNALUX  :Owend", MessageKind.Announcement, "A", "LUX")]
    [InlineData(":BLNA-WEB :Web", MessageKind.Announcement, "A", "-WEB")]
    [InlineData(":BLNq     :Odd", MessageKind.Message, null, null)]
    public void ReadsBulletinsAndAnnouncements(string information, MessageKind kind, string? id, string? group)
    {
        var message = Message(information);

        Assert.Equal(kind, message.Kind);
        Assert.Equal(id, message.BulletinId);
        Assert.Equal(group, message.BulletinGroup);
        Assert.False(message.IsAckRequired);
    }

    [Fact]
    public void ReadsNwsBulletins()
    {
        var message = Message(":NWS-WARN :092010z,THUNDER_STORM,AR_ASHLEY,{S9JbA");

        Assert.Equal(MessageKind.NwsBulletin, message.Kind);
        Assert.Equal("S9JbA", message.MessageId);
        Assert.False(message.IsAckRequired);
    }

    [Theory]
    [InlineData(":KH2Z     :?APRSD", "APRSD", null)]
    [InlineData(":KH2Z     :?PING?", "PING", null)]
    [InlineData(":KH2Z     :?APRSH N0QBF     ", "APRSH", "N0QBF")]
    [InlineData(":KH2Z     :?APRSP{17", "APRSP", null)]
    public void ReadsDirectedQueries(string information, string type, string? argument)
    {
        var message = Message(information);

        Assert.Equal(MessageKind.Query, message.Kind);
        Assert.Equal(type, message.QueryType);
        Assert.Equal(argument, message.QueryArgument);
    }

    [Fact]
    public void TextKeepsUtf8()
    {
        var packet = AprsPacket.Parse([.. "N0CALL>APRS::JA1ABC   :"u8, .. "こんにちは{7"u8]);

        var message = Assert.IsType<MessagePayload>(packet.Payload);
        Assert.Equal("こんにちは", message.Text);
        Assert.Equal("7", message.MessageId);
    }

    [Theory]
    [InlineData(":N0CALL:hi")]
    [InlineData(":N0CALL   ")]
    [InlineData(":N0CALL    hi")]
    public void AMissingAddresseeIsAnError(string information)
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:" + information);

        Assert.Equal(ParseOutcome.Malformed, packet.Outcome);
        Assert.Null(packet.Payload);
        Assert.Equal([new ParseIssue(ParseIssueCode.InvalidAddressee, ParseIssueSeverity.Error, 13)], packet.Issues);
    }

    [Fact]
    public void ReadsParameterNamesAndUnits()
    {
        var names = Decode<TelemetryDefinition>(":N0QBF-11 :PARM.Battery,Btemp,,Pres", out _);
        var units = Decode<TelemetryDefinition>(":N0QBF-11 :UNIT.v/100,deg.F", out _);

        Assert.Equal(TelemetryDefinitionKind.ParameterNames, names.Kind);
        Assert.Equal("N0QBF-11", names.Addressee);
        Assert.Equal(["Battery", "Btemp", "", "Pres"], names.Labels);
        Assert.Equal(TelemetryDefinitionKind.Units, units.Kind);
        Assert.Equal(["v/100", "deg.F"], units.Labels);
    }

    [Fact]
    public void ReadsEquationsForFewerThanFiveChannels()
    {
        var definition = Decode<TelemetryDefinition>(":N0QBF-11 :EQNS.0,0.001,0,0,.5,-32", out var packet);

        Assert.Equal([new TelemetryEquation(0, 0.001, 0), new TelemetryEquation(0, 0.5, -32)], definition.Equations);
        Assert.Equal(1034.8, new TelemetryEquation(0, 5.2, 0).Apply(199), 6);
        Assert.Equal(2, new TelemetryEquation(1, 0, 1).Apply(1));
        Assert.Empty(packet.Issues);
    }

    [Theory]
    [InlineData(":N0QBF-11 :EQNS.0,1")]
    [InlineData(":N0QBF-11 :EQNS.0,1,x")]
    [InlineData(":N0QBF-11 :EQNS.0,1,0,0,1,0,0,1,0,0,1,0,0,1,0,0,1,0")]
    [InlineData(":N0QBF-11 :BITS.1011,Project")]
    [InlineData(":N0QBF-11 :BITS.1011000x,Project")]
    public void UnreadableDefinitionsKeepTheirKindWithAWarning(string information)
    {
        var definition = Decode<TelemetryDefinition>(information, out var packet);

        Assert.Empty(definition.Equations);
        Assert.Null(definition.BitSense);
        Assert.Equal(
            [new ParseIssue(ParseIssueCode.InvalidTelemetryDefinition, ParseIssueSeverity.Warning, 28)],
            packet.Issues
        );
    }

    [Theory]
    [InlineData(":N0QBF-11 :BITS.10110000,N0QBF's Big Balloon", "N0QBF's Big Balloon")]
    [InlineData(":N0QBF-11 :BITS.10110000", null)]
    public void ReadsBitSenseAndProject(string information, string? project)
    {
        var definition = Decode<TelemetryDefinition>(information, out _);

        Assert.Equal("10110000", definition.BitSense);
        Assert.Equal(project, definition.ProjectName);
    }

    [Fact]
    public void MessageIssueCodesHaveDescriptions()
    {
        Assert.EndsWith(".", ParseIssueCode.InvalidAddressee.Describe(), StringComparison.Ordinal);
        Assert.EndsWith(".", ParseIssueCode.InvalidTelemetryDefinition.Describe(), StringComparison.Ordinal);
    }
}
