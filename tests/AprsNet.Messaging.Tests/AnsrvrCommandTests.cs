using AprsNet.Packets;

namespace AprsNet.Messaging.Tests;

public sealed class AnsrvrCommandTests
{
    public static TheoryData<AnsrvrCommand, string> Forms =>
        new()
        {
            { AnsrvrCommand.Join("NZEVENT"), "J NZEVENT" },
            { AnsrvrCommand.Leave("NZEVENT"), "U NZEVENT" },
            { AnsrvrCommand.Post("NZEVENT", "Net at 7pm on 146.52"), "CQ NZEVENT Net at 7pm on 146.52" },
            { AnsrvrCommand.KeepAlive("NZEVENT", "SATERN", "JOTA"), "K NZEVENT SATERN JOTA" },
        };

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachFormRoundTrips(AnsrvrCommand command, string text)
    {
        Assert.Equal(text, command.Text);
        Assert.Equal(text, command.ToString());

        Assert.True(AnsrvrCommand.TryParse(text, out var parsed));

        Assert.Equal(command, parsed);
        Assert.Equal(command.Kind, parsed.Kind);
        Assert.Equal(command.Groups, parsed.Groups);
        Assert.Equal(command.Message, parsed.Message);
    }

    [Fact]
    public void APostKeepsItsGroupAndText()
    {
        var post = AnsrvrCommand.Post("SOTA", "W0/FR-063 on 14.062 CW");

        Assert.Equal(AnsrvrCommandKind.Post, post.Kind);
        Assert.Equal(["SOTA"], post.Groups);
        Assert.Equal("W0/FR-063 on 14.062 CW", post.Message);
    }

    [Theory]
    [InlineData("j nzevent", AnsrvrCommandKind.Join, "nzevent")]
    [InlineData("u NZEVENT", AnsrvrCommandKind.Leave, "NZEVENT")]
    [InlineData("cq NZEVENT hello", AnsrvrCommandKind.Post, "NZEVENT")]
    [InlineData("k NZEVENT", AnsrvrCommandKind.KeepAlive, "NZEVENT")]
    public void TheCommandLettersMayBeInEitherCase(string text, AnsrvrCommandKind kind, string group)
    {
        Assert.True(AnsrvrCommand.TryParse(text, out var command));
        Assert.Equal(kind, command.Kind);
        Assert.Equal([group], command.Groups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("J")]
    [InlineData("J ")]
    [InlineData("J NZEVENT EXTRA")]
    [InlineData("CQ NZEVENT")]
    [InlineData("CQ NZEVENT  ")]
    [InlineData("K ")]
    [InlineData("K A  B")]
    [InlineData("?")]
    [InlineData("L")]
    [InlineData("Hello there")]
    [InlineData("J NZ|EVENT")]
    public void OtherTextIsNotACommand(string? text)
    {
        Assert.False(AnsrvrCommand.TryParse(text, out var command));
        Assert.Null(command);
    }

    [Fact]
    public void WhatCantBeSentIsRefused()
    {
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Join("NZ EVENT"));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Join(""));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Leave("NZ~EVENT"));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Post("NZEVENT", " "));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Post("NZEVENT", "a{b"));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.Post("NZEVENT", new string('x', 60)));
        Assert.Throws<ArgumentException>(() => AnsrvrCommand.KeepAlive());
        Assert.Throws<ArgumentException>(() =>
            AnsrvrCommand.KeepAlive(Enumerable.Range(0, 10).Select(i => $"GROUP{i}"))
        );
    }

    [Fact]
    public void ACommandGoesToAnsrvrAsAMessage()
    {
        using var station = new MessagingStation(Callsign.Parse("N0CALL-9"));

        var message = station.Send(AnsrvrCommand.Ansrvr, AnsrvrCommand.Join("NZEVENT").Text);

        Assert.Equal("ANSRVR", message.To.Value);
        Assert.Equal("J NZEVENT", message.Text);
        Assert.True(AnsrvrCommand.KeepAliveInterval < AnsrvrCommand.MembershipLifetime);
    }
}
