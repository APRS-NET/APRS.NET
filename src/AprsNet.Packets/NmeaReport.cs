namespace AprsNet.Packets;

/// <summary>
/// A raw NMEA sentence sent as an APRS packet (<c>$</c>, APRS101 p. 34): a tracker passing on its GPS's output as it
/// came. <see cref="Fix"/> has the position, speed and course the sentence gives. A packet carries one sentence, so a
/// GGA has no speed and a VTG no position.
/// </summary>
public sealed class NmeaReport : AprsPayload
{
    internal NmeaReport(NmeaSentence sentence)
    {
        Sentence = sentence;
    }

    /// <summary>The sentence.</summary>
    public NmeaSentence Sentence { get; }

    /// <summary>What the sentence says about the fix: <see cref="NmeaSentence.Fix"/>.</summary>
    public GpsFix Fix => Sentence.Fix;
}
