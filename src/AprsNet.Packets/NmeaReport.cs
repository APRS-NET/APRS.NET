namespace AprsNet.Packets;

/// <summary>
/// A raw NMEA sentence sent as an APRS packet (<c>$</c>, APRS101 p. 34): a tracker passing on its GPS's output as it
/// came. <see cref="Fix"/> has the position, speed and course the sentence gives. A packet carries one sentence, so a
/// GGA has no speed and a VTG no position.
/// </summary>
public sealed class NmeaReport : AprsPayload
{
    internal NmeaReport(NmeaSentence sentence, Callsign? source = null, Callsign? destination = null)
    {
        Sentence = sentence;
        if (
            source is not null
            && destination is not null
            && AddressSymbols.TryFromAddress(source, destination, out var symbol, out var from)
        )
        {
            Symbol = symbol;
            SymbolSource = from;
        }
    }

    /// <summary>The sentence.</summary>
    public NmeaSentence Sentence { get; }

    /// <summary>What the sentence says about the fix: <see cref="NmeaSentence.Fix"/>.</summary>
    public GpsFix Fix => Sentence.Fix;

    /// <summary>
    /// The station's symbol, taken from the packet's address since a sentence has none (APRS101 pp. 92–93): from a
    /// <c>GPSxyz</c>-style destination, or else the source's SSID. <see langword="null"/> when neither gives one. See
    /// <see cref="AddressSymbols"/>.
    /// </summary>
    public AprsSymbol? Symbol { get; }

    /// <summary>Where <see cref="Symbol"/> came from; <see langword="null"/> when there is none.</summary>
    public AddressSymbolSource? SymbolSource { get; }
}
