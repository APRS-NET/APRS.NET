using AprsNet.Packets;

namespace AprsNet.Messaging;

/// <summary>Where an <see cref="OutgoingMessage"/> has got to.</summary>
public enum OutgoingMessageState
{
    /// <summary>Waiting behind an earlier message to the same station.</summary>
    Queued,

    /// <summary>Sent once, and waiting for an ack.</summary>
    Sent,

    /// <summary>Sent again, and still waiting for an ack.</summary>
    Retrying,

    /// <summary>Acknowledged, by an ack or a reply-ack. Final, unless sent again.</summary>
    Delivered,

    /// <summary>The station answered with a rej. Final.</summary>
    Rejected,

    /// <summary>
    /// No ack came before the retries ran out. It gets one more try if the station is heard again within
    /// <see cref="MessagingStationOptions.RetryOnHeardWindow"/>.
    /// </summary>
    Failed,

    /// <summary>Cancelled by the app. Final.</summary>
    Cancelled,
}

/// <summary>
/// A message a <see cref="MessagingStation"/> is sending, from <see cref="MessagingStation.Send"/>: its ID, and where
/// it has got to. <see cref="StateChanged"/> fires on each change, on the thread that caused it.
/// </summary>
public sealed class OutgoingMessage
{
    internal OutgoingMessage(Callsign to, string text, string? messageId, RetryProfile retryProfile)
    {
        To = to;
        Text = text;
        MessageId = messageId;
        RetryProfile = retryProfile;
        SendsLeft = retryProfile.Sends;
    }

    /// <summary>The station it is for.</summary>
    public Callsign To { get; }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>The message ID, or <see langword="null"/> for a message sent without one, which is never acked.</summary>
    public string? MessageId { get; }

    /// <summary>The retry schedule it follows.</summary>
    public RetryProfile RetryProfile { get; }

    /// <summary>Where it has got to.</summary>
    public OutgoingMessageState State { get; private set; }

    /// <summary>How many times it has been sent.</summary>
    public int Attempts { get; internal set; }

    /// <summary>When it was last sent, or <see langword="null"/> before the first send.</summary>
    public DateTimeOffset? LastSent { get; internal set; }

    /// <summary>
    /// When it will next be sent, or when it fails if no more retries are left; <see langword="null"/> when it is
    /// queued or finished.
    /// </summary>
    public DateTimeOffset? NextDue { get; internal set; }

    /// <summary>Whether it has finished: delivered, rejected, failed or cancelled.</summary>
    public bool IsFinished =>
        State
            is OutgoingMessageState.Delivered
                or OutgoingMessageState.Rejected
                or OutgoingMessageState.Failed
                or OutgoingMessageState.Cancelled;

    /// <summary>Fires when <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>When it failed, for retrying when the station is heard again.</summary>
    internal DateTimeOffset? FailedAt { get; set; }

    /// <summary>How many more times it may be sent before it fails.</summary>
    internal int SendsLeft { get; set; }

    /// <summary>Whether it already had its extra try for being heard again.</summary>
    internal bool RetriedOnHeard { get; set; }

    /// <summary>Sets the state, and returns the event to raise once the station's lock is released.</summary>
    internal Action? SetState(OutgoingMessageState state)
    {
        if (State == state)
        {
            return null;
        }

        State = state;
        return () => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public override string ToString() => $"to {To}: {Text} ({MessageId ?? "no ID"}, {State})";
}
