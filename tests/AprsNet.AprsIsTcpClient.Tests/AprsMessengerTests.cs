using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsMessengerTests
{
    private static readonly PacketAddress _address = PacketAddress.Parse("N0CALL-9", "APZ123", []);
    private readonly FakeTimeProvider _time = new();
    private readonly List<AprsPacket> _sent = [];

    private AprsMessenger Messenger(params TimeSpan[] retryDelays)
    {
        ValueTask Send(AprsPacket packet, CancellationToken cancellationToken)
        {
            lock (_sent)
            {
                _sent.Add(packet);
            }

            return ValueTask.CompletedTask;
        }

        return retryDelays.Length == 0
            ? new AprsMessenger(_address, Send, _time)
            : new AprsMessenger(_address, Send, _time) { RetryDelays = retryDelays };
    }

    private static AprsPacket Received(string line) => AprsPacket.Parse(line);

    private async Task WaitForSent(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (_sent)
            {
                if (_sent.Count >= count)
                {
                    return;
                }
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    private string SentId(int index) => ((MessagePayload)_sent[index].Payload!).MessageId!;

    [Fact]
    public async Task SendsTheMessageWithAnIdAndFinishesOnTheAck()
    {
        using var messenger = Messenger();

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        var id = SentId(0);
        Assert.Equal($"N0CALL-9>APZ123::WU2Z     :Testing{{{id}", _sent[0].ToString());

        await messenger.HandleAsync(Received($"WU2Z>APRS::N0CALL-9 :ack{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(new MessageDelivery(id, MessageDeliveryStatus.Acknowledged, 1), await delivery);
    }

    [Fact]
    public async Task ResendsUntilAcknowledged()
    {
        using var messenger = Messenger();

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        _time.Advance(TimeSpan.FromSeconds(30));
        await WaitForSent(2);
        Assert.Equal(_sent[0].ToString(), _sent[1].ToString()); // the same message, same ID
        await messenger.HandleAsync(
            Received($"WU2Z>APRS::N0CALL-9 :ack{SentId(0)}"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, (await delivery).Attempts);
    }

    [Fact]
    public async Task GivesUpAfterTheLastRetryDelay()
    {
        using var messenger = Messenger(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForSent(2);
        _time.Advance(TimeSpan.FromSeconds(2));

        var result = await delivery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(MessageDeliveryStatus.NotAcknowledged, result.Status);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public void TheDefaultScheduleIsFiveAttemptsDoublingFromThirtySeconds()
    {
        using var messenger = Messenger();

        Assert.Equal([30, 60, 120, 240, 480], messenger.RetryDelays.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task FinishesOnARejection()
    {
        using var messenger = Messenger();

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        await messenger.HandleAsync(
            Received($"WU2Z>APRS::N0CALL-9 :rej{SentId(0)}"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageDeliveryStatus.Rejected, (await delivery).Status);
    }

    [Fact]
    public async Task AReplyAckCountsAsAnAcknowledgement()
    {
        using var messenger = Messenger();

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        var reply = await messenger.HandleAsync(
            Received($"WU2Z>APRS::N0CALL-9 :Got it{{7}}{SentId(0)}"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageDeliveryStatus.Acknowledged, (await delivery).Status);
        Assert.Equal("Got it", reply!.Text);
    }

    [Fact]
    public async Task IgnoresAnAckFromAnotherStation()
    {
        using var messenger = Messenger(TimeSpan.FromSeconds(1));

        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        await messenger.HandleAsync(
            Received($"N1OTHER>APRS::N0CALL-9 :ack{SentId(0)}"),
            TestContext.Current.CancellationToken
        );
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(MessageDeliveryStatus.NotAcknowledged, (await delivery).Status);
    }

    [Fact]
    public async Task MatchesCallsignsWithoutRegardToCase()
    {
        using var messenger = Messenger();

        var delivery = messenger.SendAsync("wu2z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);
        await messenger.HandleAsync(
            Received($"WU2Z>APRS::n0call-9 :ack{SentId(0)}"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(MessageDeliveryStatus.Acknowledged, (await delivery).Status);
    }

    [Fact]
    public async Task AcknowledgesAnIncomingMessageAndReportsItOnce()
    {
        using var messenger = Messenger();
        var incoming = Received("WU2Z>APRS::N0CALL-9 :Hello{42");

        var first = await messenger.HandleAsync(incoming, TestContext.Current.CancellationToken);
        var repeat = await messenger.HandleAsync(incoming, TestContext.Current.CancellationToken);

        Assert.Equal("Hello", first!.Text);
        Assert.Null(repeat);
        Assert.Equal(2, _sent.Count); // a repeat is acknowledged again: the first ack may have been lost
        Assert.All(_sent, ack => Assert.Equal("N0CALL-9>APZ123::WU2Z     :ack42", ack.ToString()));
    }

    [Fact]
    public async Task AMessageWithoutAnIdIsReportedButNotAcknowledged()
    {
        using var messenger = Messenger();

        var message = await messenger.HandleAsync(
            Received("WU2Z>APRS::N0CALL-9 :No reply needed"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("No reply needed", message!.Text);
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData("WU2Z>APRS::N1OTHER  :Not for us{1")]
    [InlineData("WU2Z>APRS::BLN1     :A bulletin")]
    [InlineData("WU2Z>APRS:>Just a status")]
    public async Task IgnoresPacketsThatAreNotMessagesToThisStation(string line)
    {
        using var messenger = Messenger();

        Assert.Null(await messenger.HandleAsync(Received(line), TestContext.Current.CancellationToken));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task RefusesAMessageThatCannotBeSentBeforeSendingAnything()
    {
        using var messenger = Messenger();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            messenger.SendAsync("WU2Z", new string('x', 68), TestContext.Current.CancellationToken)
        );
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task DisposingCancelsPendingSends()
    {
        var messenger = Messenger();
        var delivery = messenger.SendAsync("WU2Z", "Testing", TestContext.Current.CancellationToken);
        await WaitForSent(1);

        messenger.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            messenger.SendAsync("WU2Z", "Again", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task GivesEachMessageItsOwnId()
    {
        using var messenger = Messenger();

        _ = messenger.SendAsync("WU2Z", "One", TestContext.Current.CancellationToken);
        _ = messenger.SendAsync("WU2Z", "Two", TestContext.Current.CancellationToken);
        await WaitForSent(2);

        Assert.NotEqual(SentId(0), SentId(1));
        Assert.All([SentId(0), SentId(1)], id => Assert.InRange(id.Length, 1, 5));
    }
}
