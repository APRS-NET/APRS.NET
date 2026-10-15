using System.Text;
using AprsNet.Packets;
using BenchmarkDotNet.Attributes;

namespace AprsNet.Packets.Benchmarks;

/// <summary>Parsing one line of each common kind, from bytes as a TNC or capture file gives them.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private static readonly Dictionary<string, byte[]> _lines = new()
    {
        ["position"] = Line("N0CALL-9>APRS,WIDE1-1,qAR,N0CALL-10:!4903.50N/07201.75W>088/036/A=001234Mobile"),
        ["compressed"] = Line("N0CALL-9>APRS,TCPIP*,qAC,T2TEST:=/5L!!<*e7>7P[Compressed"),
        ["mic-e"] = Line("N0CALL-9>S32U6T,WIDE1-1,qAR,N0CALL-10:`(_fn\"Oj/]\"4T}In service=\r"),
        ["message"] = Line("N0CALL-9>APRS,TCPIP*,qAC,T2TEST::WU2Z     :Testing{003"),
        ["weather"] = Line(
            "N0CALL-13>APRS,TCPIP*,qAC,T2TEST:@092345z4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900wRSW"
        ),
        ["object"] = Line("N0CALL-9>APRS,TCPIP*,qAC,T2TEST:;LEADER   *092345z4903.50N/07201.75W>088/036"),
        ["telemetry"] = Line("N0CALL-11>APRS,TCPIP*,qAC,T2TEST:T#005,199,000,255,073,123,01101001"),
        ["status"] = Line("N0CALL-9>APRS,TCPIP*,qAC,T2TEST:>092345zNet control tonight"),
    };

    [ParamsSource(nameof(Kinds))]
    public string Kind { get; set; } = "position";

    public static IEnumerable<string> Kinds => _lines.Keys;

    [Benchmark]
    public AprsPacket Parse() => AprsPacket.Parse(_lines[Kind]);

    private static byte[] Line(string text) => Encoding.Latin1.GetBytes(text);
}
