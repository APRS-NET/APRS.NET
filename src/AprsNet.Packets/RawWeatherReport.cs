namespace AprsNet.Packets;

/// <summary>
/// Raw data from a stand-alone weather station (APRS101 ch. 12): the station's own serial output, sent as it came.
/// APRS101 names the data type identifiers but not the field layouts, which belong to the manufacturers, so the
/// readings are not decoded. APRS 1.1 discourages raw formats in favour of positionless or complete weather reports.
/// </summary>
public sealed class RawWeatherReport : AprsPayload
{
    internal RawWeatherReport() { }

    /// <summary>Which station and output mode sent the data, from the data type identifier.</summary>
    public RawWeatherFormat Format { get; internal init; }

    /// <summary>
    /// The station's data after the identifier (<c>!!</c>, <c>$ULTW</c>, <c>#</c> or <c>*</c>), exactly as sent:
    /// hex digits, with dashes where the station has no reading.
    /// </summary>
    public string Data { get; internal init; } = "";
}

/// <summary>The weather station and output mode of a <see cref="RawWeatherReport"/>.</summary>
public enum RawWeatherFormat
{
    /// <summary>Peet Bros Ultimeter 2000 in data logging mode: <c>!!</c>.</summary>
    UltimeterDataLogging,

    /// <summary>Peet Bros Ultimeter 2000 in packet mode: <c>$ULTW</c>.</summary>
    UltimeterPacket,

    /// <summary>Peet Bros U-II: <c>#</c> or <c>*</c> followed by hex digits.</summary>
    PeetBrosUII,
}
