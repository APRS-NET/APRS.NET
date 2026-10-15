namespace AprsNet.Packets;

/// <summary>Which telemetry definition a <see cref="TelemetryDefinition"/> carries (APRS101 ch. 13).</summary>
public enum TelemetryDefinitionKind
{
    /// <summary><c>PARM.</c>: names for the analog and digital channels.</summary>
    ParameterNames,

    /// <summary><c>UNIT.</c>: units or labels for the analog and digital channels.</summary>
    Units,

    /// <summary><c>EQNS.</c>: coefficients that turn raw analog values into readings.</summary>
    Equations,

    /// <summary><c>BITS.</c>: which state of each digital channel is "on", and the project name.</summary>
    BitSense,
}

/// <summary>
/// One analog channel's conversion: reading = <see cref="A"/> × value² + <see cref="B"/> × value + <see cref="C"/>.
/// </summary>
/// <param name="A">The squared term.</param>
/// <param name="B">The linear term.</param>
/// <param name="C">The offset.</param>
public readonly record struct TelemetryEquation(double A, double B, double C)
{
    /// <summary>Applies the equation to a raw channel value.</summary>
    public double Apply(double value) => (A * value * value) + (B * value) + C;
}

/// <summary>
/// A telemetry definition (APRS101 ch. 13): sent as a message to the telemetry station's own callsign, it names,
/// labels and scales the channels of that station's <c>T#</c> reports.
/// </summary>
/// <remarks>
/// To send one, set <see cref="Addressee"/> to the telemetry station's callsign, <see cref="Kind"/>, and the
/// <see cref="Labels"/>, <see cref="Equations"/> or <see cref="BitSense"/> that kind carries; the whole text must fit
/// in a 67-character message. APRS101 also suggests shorter names for the later channels, which receivers do not
/// enforce.
/// </remarks>
public sealed class TelemetryDefinition : AprsPayload
{
    /// <summary>Creates a telemetry definition to encode with <see cref="AprsPacket.Create"/>.</summary>
    public TelemetryDefinition() { }

    /// <summary>The message text as sent, so a parsed definition is rebuilt with its original spelling.</summary>
    internal ReadOnlyMemory<byte> TextBytes { get; init; }

    /// <summary>The station whose telemetry this defines (the message addressee, without padding).</summary>
    public string Addressee { get; init; } = "";

    /// <summary>Which definition this is.</summary>
    public TelemetryDefinitionKind Kind { get; init; }

    /// <summary>
    /// Channel names (<c>PARM.</c>) or units (<c>UNIT.</c>) in channel order: up to five analog channels, then up to
    /// eight digital ones. Empty for the other kinds.
    /// </summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The analog channel equations (<c>EQNS.</c>), in channel order; empty for the other kinds.</summary>
    public IReadOnlyList<TelemetryEquation> Equations { get; init; } = [];

    /// <summary>
    /// For <c>BITS.</c>: eight <c>0</c>/<c>1</c> characters, one per digital channel, giving the state that means "on".
    /// </summary>
    public string? BitSense { get; init; }

    /// <summary>For <c>BITS.</c>: the project name that follows the bit sense.</summary>
    public string? ProjectName { get; init; }
}
