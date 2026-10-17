using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Messaging.Tests;

public sealed class MessagingStationTests : IDisposable
{
    private static readonly PacketAddress Address = PacketAddress.Parse("N0CALL-9", "APZNET", []);
    private static readonly Callsign Wu2z = Callsign.Parse("WU2Z");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly List<IncomingMessage> _received = [];
    private MessagingStation _station;

    public MessagingStationTests() => _station = Start(new MessagingStationOptions());

    public void Dispose() => _station.Dispose();

    private MessagingStation Start(MessagingStationOptions options)
    {
        options.TimeProvider = _time;
        var station = new MessagingStation(Callsign.Parse("N0CALL-9"), options);
        station.MessageReceived += (_, e) => _received.Add(e.Message);
        return station;
    }

    private void Restart(MessagingStationOptions options)
    {
        _station.Dispose();
        _station = Start(options);
    }

    /// <summary>The information fields sent since the last call, such as <c>:WU2Z     :Hello{01}</c>.</summary>
    private List<string> Sent()
    {
        var sent = new List<string>();
        while (_station.TryTakeOutgoing(out var packet))
        {
            var line = Address.Create(packet.Payload).ToString();
            sent.Add(line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        }

        return sent;
    }

    private void Hear(string line, string? source = "rf") => _station.Receive(AprsPacket.Parse(line), source);

    private void Wait(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void AMessageGoesOutAtOnceWithAReplyAckId()
    {
        var message = _station.Send(Wu2z, "Hello");
        Assert.Equal([":WU2Z     :Hello{01}"], Sent());
        Assert.Equal("01", message.MessageId);
        Assert.Equal(OutgoingMessageState.Sent, message.State);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public void AnAckAfterTwoRetriesDeliversIt()
    {
        var states = new List<OutgoingMessageState>();
        var message = _station.Send(Wu2z, "Hello");
        message.StateChanged += (_, _) => states.Add(message.State);
        Sent();

        Wait(29.9);
        Assert.Empty(Sent());
        Wait(3.2);
        Assert.Equal([":WU2Z     :Hello{01}"], Sent());
        Wait(66);
        Assert.Equal([":WU2Z     :Hello{01}"], Sent());
        Assert.Equal(3, message.Attempts);

        Hear("WU2Z>APRS::N0CALL-9 :ack01");
        Assert.Equal(OutgoingMessageState.Delivered, message.State);
        Assert.Null(message.NextDue);
        Wait(600);
        Assert.Empty(Sent());
        Assert.Equal([OutgoingMessageState.Retrying, OutgoingMessageState.Delivered], states);
    }

    [Fact]
    public void ARejStopsItAtOnce()
    {
        var message = _station.Send(Wu2z, "Hello");
        Hear("WU2Z>APRS::N0CALL-9 :rej01");
        Assert.Equal(OutgoingMessageState.Rejected, message.State);
        Sent();
        Wait(600);
        Assert.Empty(Sent());
    }

    [Fact]
    public void AReplyAckCountsAsAnAck()
    {
        var message = _station.Send(Wu2z, "Hello");
        Hear("WU2Z>APRS::N0CALL-9 :Hi back{7}01");
        Assert.Equal(OutgoingMessageState.Delivered, message.State);
    }

    [Fact]
    public void AnAckMatchesInAnyCase()
    {
        Restart(new MessagingStationOptions { LastMessageId = "a0" });
        var message = _station.Send(Wu2z, "Hello");
        Assert.Equal("a1", message.MessageId);
        Hear("WU2Z>APRS::N0CALL-9 :ackA1");
        Assert.Equal(OutgoingMessageState.Delivered, message.State);
    }

    [Fact]
    public void ItFailsWhenTheRetriesRunOutAndTheNextMessageGoesOut()
    {
        var first = _station.Send(Wu2z, "One", RetryProfile.AprsIs);
        var second = _station.Send(Wu2z, "Two", RetryProfile.None);
        Assert.Equal(OutgoingMessageState.Queued, second.State);
        Assert.Equal([":WU2Z     :One{01}"], Sent());

        // 30 s, 2 min and 5 min (each up to 10 % longer), then 5 min for the ack.
        Wait(34);
        Wait(133);
        Wait(331);
        Assert.Equal(4, first.Attempts);
        Assert.Equal(OutgoingMessageState.Retrying, first.State);
        Assert.Equal(3, Sent().Count);

        Wait(300);
        Assert.Equal(OutgoingMessageState.Failed, first.State);
        Assert.Equal(OutgoingMessageState.Sent, second.State);
        Assert.Equal([":WU2Z     :Two{02}"], Sent());

        Wait(120);
        Assert.Equal(OutgoingMessageState.Failed, second.State);
        Assert.Empty(Sent());
    }

    [Fact]
    public void MessagesToDifferentStationsGoOutSideBySide()
    {
        _station.Send(Wu2z, "One");
        _station.Send(Callsign.Parse("KB2ICI-14"), "Two");
        Assert.Equal([":WU2Z     :One{01}", ":KB2ICI-14:Two{02}"], Sent());
    }

    [Fact]
    public void NoRetryComesInsideTheDuplicateWindow()
    {
        var profile = new RetryProfile("Quick", [TimeSpan.FromSeconds(30)], TimeSpan.FromSeconds(30));
        for (var i = 0; i < 20; i++)
        {
            var message = _station.Send(Callsign.Parse($"TEST{i}"), "Hello", profile);
            var firstSent = message.LastSent;
            Assert.True(message.NextDue - firstSent >= RetryProfile.MinimumGap);
            Assert.True(message.NextDue - firstSent <= TimeSpan.FromSeconds(33));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryProfile("Fast", [TimeSpan.FromSeconds(15)], TimeSpan.FromMinutes(1))
        );
    }

    [Fact]
    public void TheAckOwedRidesOnTheNextMessage()
    {
        Hear("WU2Z>APRS::N0CALL-9 :Are you there?{AB");
        Assert.Equal([":WU2Z     :ackAB"], Sent());
        _station.Send(Wu2z, "Yes");
        Assert.Equal([":WU2Z     :Yes{01}AB"], Sent());
    }

    [Fact]
    public void IdsCarryOnAfterARestart()
    {
        var saved = new List<string?>();
        _station.LastMessageIdChanged += (_, _) => saved.Add(_station.LastMessageId);
        _station.Send(Wu2z, "One");
        _station.Send(Callsign.Parse("KB2ICI"), "Two");
        Assert.Equal(["01", "02"], saved);

        Restart(new MessagingStationOptions { LastMessageId = _station.LastMessageId });
        Assert.Equal("03", _station.Send(Wu2z, "Three").MessageId);

        Restart(new MessagingStationOptions { LastMessageId = "zz" });
        Assert.Equal("01", _station.Send(Wu2z, "Wrapped").MessageId);
    }

    [Fact]
    public void AFailedMessageIsTriedAgainWhenItsStationIsHeard()
    {
        var message = _station.Send(Wu2z, "Hello", RetryProfile.None);
        Wait(120);
        Assert.Equal(OutgoingMessageState.Failed, message.State);
        Sent();

        Hear("WU2Z>APRS:>Back on the air");
        Assert.Equal(OutgoingMessageState.Retrying, message.State);
        Assert.Equal([":WU2Z     :Hello{01}"], Sent());
        Assert.Equal(2, message.Attempts);

        // Once only: it fails again, and being heard again doesn't revive it.
        Wait(120);
        Assert.Equal(OutgoingMessageState.Failed, message.State);
        Hear("WU2Z>APRS:>Still here");
        Assert.Empty(Sent());
    }

    [Fact]
    public void AFailedMessageStaysFailedWhenItsStationIsHeardTooLate()
    {
        var message = _station.Send(Wu2z, "Hello", RetryProfile.None);
        Wait(120);
        Wait(3600);
        Sent();
        Hear("WU2Z>APRS:>Back");
        Assert.Equal(OutgoingMessageState.Failed, message.State);
        Assert.Empty(Sent());
    }

    [Fact]
    public void ALateAckDeliversAFailedMessage()
    {
        var message = _station.Send(Wu2z, "Hello", RetryProfile.None);
        Wait(120);
        Hear("WU2Z>APRS::N0CALL-9 :ack01");
        Assert.Equal(OutgoingMessageState.Delivered, message.State);
    }

    [Fact]
    public void CancellingStopsItAndStartsTheNext()
    {
        var first = _station.Send(Wu2z, "One");
        var second = _station.Send(Wu2z, "Two");
        Sent();
        _station.Cancel(first);
        Assert.Equal(OutgoingMessageState.Cancelled, first.State);
        Assert.Equal([":WU2Z     :Two{02}"], Sent());
        Assert.Equal(OutgoingMessageState.Sent, second.State);
    }

    [Fact]
    public void AMessageWithoutAnIdGoesOutOnceAndDoesNotHoldUpOthers()
    {
        var auto = _station.Send(Wu2z, "AA:Away from the radio", withId: false);
        var real = _station.Send(Wu2z, "Hello");
        Assert.Null(auto.MessageId);
        Assert.Equal([":WU2Z     :AA:Away from the radio", ":WU2Z     :Hello{01}"], Sent());
        Wait(600);
        Assert.DoesNotContain(":WU2Z     :AA:Away from the radio", Sent());
        Assert.Equal(OutgoingMessageState.Sent, auto.State);
        Assert.Equal("01", real.MessageId);
    }

    [Theory]
    [InlineData("A message that is far too long to fit in the sixty-seven characters APRS allows")]
    [InlineData("Pipe | here")]
    [InlineData("Tilde ~ here")]
    [InlineData("Brace { here")]
    public void UnsendableTextIsRefused(string text) =>
        Assert.Throws<ArgumentException>(() => _station.Send(Wu2z, text));

    [Fact]
    public void RepliesGoBackTheWayTheStationWasHeard()
    {
        Hear("WU2Z>APRS:>Status", source: "tnc");
        _station.Send(Wu2z, "Hello");
        Assert.True(_station.TryTakeOutgoing(out var packet));
        Assert.Equal("tnc", packet.Source);
        Assert.Equal(Wu2z, packet.To);

        _station.Send(Callsign.Parse("KB2ICI"), "Never heard");
        Assert.True(_station.TryTakeOutgoing(out packet));
        Assert.Null(packet.Source);
    }

    [Fact]
    public void EveryCopyIsAckedButDeliveredOnce()
    {
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12", "rf");
        Hear("WU2Z>APRS,TCPIP*,qAC,T2TEST::N0CALL-9 :Hello{12", "aprs-is");
        Wait(60);
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12", "rf");
        Assert.Equal([":WU2Z     :ack12", ":WU2Z     :ack12", ":WU2Z     :ack12"], Sent());
        var message = Assert.Single(_received);
        Assert.Equal(Wu2z, message.From);
        Assert.Equal("Hello", message.Text);
        Assert.Equal("12", message.MessageId);
        Assert.Equal("rf", message.Source);
    }

    [Fact]
    public void AnIdIsDeliveredAgainAfterTheReceiveWindow()
    {
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12");
        Wait(3601);
        Hear("WU2Z>APRS::N0CALL-9 :Hello again{12");
        Assert.Equal(["Hello", "Hello again"], _received.Select(m => m.Text));
    }

    [Fact]
    public void AMessageWithoutAnIdIsDeliveredAndNotAcked()
    {
        Hear("WU2Z>APRS::N0CALL-9 :No reply needed");
        Assert.Empty(Sent());
        var message = Assert.Single(_received);
        Assert.Null(message.MessageId);
    }

    [Theory]
    [InlineData("{12", ":WU2Z     :ack12")]
    [InlineData("{12}", ":WU2Z     :ack12}")]
    [InlineData("{12}AB", ":WU2Z     :ack12}AB")]
    public void TheAckEchoesTheWholeId(string id, string ack)
    {
        // The test vectors in docs/spec/aprs11/messaging.md.
        Hear($"WU2Z>APRS::N0CALL-9 :Hello{id}");
        Assert.Equal([ack], Sent());
        Assert.Equal("12", Assert.Single(_received).MessageId);
    }

    [Fact]
    public void ReplyAckCopiesAreDeliveredOnce()
    {
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12}");
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12}AB");
        Assert.Single(_received);
    }

    [Fact]
    public void AckRepeatsAreSentThirtySecondsApart()
    {
        Restart(new MessagingStationOptions { AckRepeats = 2 });
        Hear("WU2Z>APRS::N0CALL-9 :Hello{12");
        Assert.Equal([":WU2Z     :ack12"], Sent());
        Wait(29);
        Assert.Empty(Sent());
        Wait(1);
        Assert.Equal([":WU2Z     :ack12"], Sent());
    }

    [Fact]
    public void TheAppCanRejectAMessage()
    {
        Hear("WU2Z>APRS::N0CALL-9 :Do something odd{12}AB", "tnc");
        Sent();
        _station.Reject(Assert.Single(_received));
        Assert.True(_station.TryTakeOutgoing(out var packet));
        Assert.Equal(MessageKind.Rejection, packet.Payload.Kind);
        Assert.Equal("12", packet.Payload.MessageId);
        Assert.Equal("AB", packet.Payload.ReplyAck);
        Assert.Equal("tnc", packet.Source);
    }

    [Fact]
    public void MessagesForOtherStationsAreIgnored()
    {
        Hear("WU2Z>APRS::N0CALL-8 :Not for us{12");
        Hear("WU2Z>APRS::BLN1     :A bulletin");
        Assert.Empty(Sent());
        Assert.Empty(_received);
    }

    [Fact]
    public void TheCallsignMatchesInAnyCaseAndSsidZeroIsNone()
    {
        Restart(new MessagingStationOptions());
        using var plain = new MessagingStation(Callsign.Parse("N0CALL"), new() { TimeProvider = _time });
        var received = 0;
        plain.MessageReceived += (_, _) => received++;
        plain.Receive(AprsPacket.Parse("WU2Z>APRS::N0CALL-0 :One{1"));
        plain.Receive(AprsPacket.Parse("WU2Z>APRS::n0call   :Two{2"));
        Assert.Equal(2, received);
    }

    [Fact]
    public void AMessageGatedAsThirdPartyIsReadForThePacketItCarries()
    {
        Hear("IGATE>APRS,WIDE1-1:}WU2Z>APRS,TCPIP,IGATE*::N0CALL-9 :Via the IGate{12");
        Assert.Equal([":WU2Z     :ack12"], Sent());
        Assert.Equal("Via the IGate", Assert.Single(_received).Text);
    }

    [Fact]
    public async Task DisposingEndsTheOutgoingStream()
    {
        _station.Send(Wu2z, "Hello");
        _station.Dispose();
        var packets = new List<OutgoingPacket>();
        await foreach (var packet in _station.ReadOutgoingAsync(TestContext.Current.CancellationToken))
        {
            packets.Add(packet);
        }

        Assert.Single(packets);
        Assert.Throws<ObjectDisposedException>(() => _station.Send(Wu2z, "Too late"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("ABC")]
    [InlineData("A!")]
    public void AnUnusableLastMessageIdIsRefused(string id) =>
        Assert.Throws<ArgumentException>(() =>
            new MessagingStation(Wu2z, new MessagingStationOptions { LastMessageId = id })
        );
}
