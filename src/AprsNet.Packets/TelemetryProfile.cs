namespace AprsNet.Packets;

/// <summary>
/// What a station's telemetry definitions say about its channels: the latest <c>PARM.</c>, <c>UNIT.</c>,
/// <c>EQNS.</c> and <c>BITS.</c> it has had. <see cref="Apply"/> turns its raw <c>T#</c> reports, or comment telemetry,
/// into named, scaled readings.
/// </summary>
/// <remarks>
/// Definitions are messages addressed to the telemetry station, and may come from another callsign, so keep one
/// profile per <see cref="TelemetryDefinition.Addressee"/> and look it up by the report's source. The profile is
/// immutable: <see cref="With"/> returns a new one, and the caller keeps it.
/// </remarks>
public sealed record TelemetryProfile
{
    /// <summary>The number of analog channels a report can carry.</summary>
    public const int AnalogChannels = 5;

    /// <summary>The number of digital channels a report can carry.</summary>
    public const int DigitalChannels = 8;

    /// <summary>A profile with no definitions: readings are the raw values, unnamed.</summary>
    public static TelemetryProfile Empty { get; } = new();

    /// <summary>Channel names from the latest <c>PARM.</c>: the analog channels, then the digital ones.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Units or labels from the latest <c>UNIT.</c>: the analog channels, then the digital ones.</summary>
    public IReadOnlyList<string> Units { get; init; } = [];

    /// <summary>The analog equations from the latest valid <c>EQNS.</c>, which may cover fewer than five channels.</summary>
    public IReadOnlyList<TelemetryEquation> Equations { get; init; } = [];

    /// <summary>The bit sense from the latest valid <c>BITS.</c>: eight <c>0</c>/<c>1</c> characters.</summary>
    public string? BitSense { get; init; }

    /// <summary>The project name from the latest valid <c>BITS.</c>.</summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// This profile updated with <paramref name="definition"/>, which replaces the earlier definition of its kind. An
    /// <c>EQNS.</c> or <c>BITS.</c> that could not be read is ignored, so the earlier one stays.
    /// </summary>
    public TelemetryProfile With(TelemetryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Kind switch
        {
            TelemetryDefinitionKind.ParameterNames => this with { Names = definition.Labels },
            TelemetryDefinitionKind.Units => this with { Units = definition.Labels },
            TelemetryDefinitionKind.Equations when definition.Equations.Count > 0 => this with
            {
                Equations = definition.Equations,
            },
            TelemetryDefinitionKind.BitSense when definition.BitSense is not null => this with
            {
                BitSense = definition.BitSense,
                ProjectName = definition.ProjectName,
            },
            _ => this,
        };
    }

    /// <summary>
    /// The readings in <paramref name="report"/>: each analog value through its equation, with its name and unit, and
    /// each digital channel with its name, label and whether it is on. A channel with no definition keeps its raw
    /// value and has no name.
    /// </summary>
    public TelemetryReading Apply(TelemetryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var analog = new TelemetryAnalogReading[report.Analog.Count];
        for (var i = 0; i < analog.Length; i++)
        {
            var raw = report.Analog[i];
            var equation = i < Equations.Count ? Equations[i] : (TelemetryEquation?)null;
            analog[i] = new TelemetryAnalogReading(
                i,
                Label(Names, i),
                Label(Units, i),
                raw,
                raw is { } value && equation is { } e ? e.Apply(value) : raw,
                equation is not null
            );
        }

        var digital = new TelemetryDigitalReading[report.Digital.Count];
        for (var i = 0; i < digital.Length; i++)
        {
            var bit = report.Digital[i];
            var onWhenSet = BitSense is null || BitSense[i] == '1';
            digital[i] = new TelemetryDigitalReading(
                i,
                Label(Names, AnalogChannels + i),
                Label(Units, AnalogChannels + i),
                bit,
                bit == onWhenSet
            );
        }

        return new TelemetryReading(report.SequenceNumber, analog, digital, ProjectName);
    }

    private static string? Label(IReadOnlyList<string> labels, int index) =>
        index < labels.Count && labels[index].Trim() is { Length: > 0 } label ? label : null;
}

/// <summary>A telemetry report with its station's definitions applied (<see cref="TelemetryProfile.Apply"/>).</summary>
/// <param name="SequenceNumber">The report's sequence number, or <see langword="null"/> for <c>MIC</c>.</param>
/// <param name="Analog">The analog channels in order.</param>
/// <param name="Digital">The digital channels in order; empty when the report has none.</param>
/// <param name="ProjectName">The project name from the station's <c>BITS.</c>, if any.</param>
public sealed record TelemetryReading(
    int? SequenceNumber,
    IReadOnlyList<TelemetryAnalogReading> Analog,
    IReadOnlyList<TelemetryDigitalReading> Digital,
    string? ProjectName
);

/// <summary>One analog channel's reading.</summary>
/// <param name="Channel">The channel, 0 to 4.</param>
/// <param name="Name">The channel's name from <c>PARM.</c>, or <see langword="null"/>.</param>
/// <param name="Unit">The channel's unit from <c>UNIT.</c>, or <see langword="null"/>.</param>
/// <param name="Raw">The value as sent, or <see langword="null"/> for an empty field.</param>
/// <param name="Value">The value through the channel's equation, or the raw value when it has none.</param>
/// <param name="IsScaled">Whether an <c>EQNS.</c> equation was applied.</param>
public readonly record struct TelemetryAnalogReading(
    int Channel,
    string? Name,
    string? Unit,
    double? Raw,
    double? Value,
    bool IsScaled
);

/// <summary>One digital channel's reading.</summary>
/// <param name="Channel">The channel, 0 to 7.</param>
/// <param name="Name">The channel's name from <c>PARM.</c>, or <see langword="null"/>.</param>
/// <param name="Label">The channel's label from <c>UNIT.</c>, such as <c>on</c> or <c>open</c>, or <see langword="null"/>.</param>
/// <param name="Bit">The bit as sent.</param>
/// <param name="IsOn">
/// Whether the channel is on: the bit equals the channel's <c>BITS.</c> sense, or is set when there is no
/// <c>BITS.</c>.
/// </param>
public readonly record struct TelemetryDigitalReading(int Channel, string? Name, string? Label, bool Bit, bool IsOn);
