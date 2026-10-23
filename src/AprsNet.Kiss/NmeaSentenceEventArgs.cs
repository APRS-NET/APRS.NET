using AprsNet.Packets;

namespace AprsNet.Kiss;

/// <summary>A GPS sentence a TNC printed: see <see cref="KissConnection.NmeaSentenceReceived"/>.</summary>
public sealed class NmeaSentenceEventArgs : EventArgs
{
    internal NmeaSentenceEventArgs(NmeaSentence sentence, DateTimeOffset at)
    {
        Sentence = sentence;
        At = at;
    }

    /// <summary>The sentence, parsed; <see cref="NmeaSentence.Text"/> is the line as the TNC printed it.</summary>
    public NmeaSentence Sentence { get; }

    /// <summary>When it arrived, by the connection's clock.</summary>
    public DateTimeOffset At { get; }
}

/// <summary>A TNC starting or stopping sending GPS sentences: see <see cref="KissConnection.GpsPresenceChanged"/>.</summary>
public sealed class GpsPresenceChangedEventArgs : EventArgs
{
    internal GpsPresenceChangedEventArgs(bool isPresent, TimeSpan timeout)
    {
        IsPresent = isPresent;
        Timeout = timeout;
    }

    /// <summary>
    /// Whether the TNC is sending GPS sentences now: <see langword="true"/> at the first one, <see langword="false"/>
    /// when none has come for <see cref="Timeout"/>.
    /// </summary>
    public bool IsPresent { get; }

    /// <summary>How long without a sentence counts as gone: the connection's <c>GpsTimeout</c>.</summary>
    public TimeSpan Timeout { get; }
}
