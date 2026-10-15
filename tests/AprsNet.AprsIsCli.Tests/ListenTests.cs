using System.Text;
using AprsNet.AprsIsTcpClient.Tests;
using Microsoft.Extensions.Logging;

namespace AprsNet.AprsIsCli.Tests;

public class ListenTests
{
    private static readonly string[] _packets =
    [
        "W1AW>APRS,TCPIP*:>one",
        "K1ABC>APRS,TCPIP*:>two",
        "N2XYZ>APRS,TCPIP*:>three",
    ];

    private static CliHarness WithPackets(params string[] lines)
    {
        var harness = new CliHarness().LoginAccepted();
        foreach (var line in lines)
            harness.Transport.Server(line);

        return harness;
    }

    [Fact]
    public async Task PrintsRawLinesAndStopsAtCount()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "--callsign", "NZ0P", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task SendsTheCallsignFilterAndSoftwareNameInTheLogin()
    {
        var harness = WithPackets(_packets);

        await harness.RunAsync(
            ["listen", "-c", "nz0p", "-f", "r/35/-97/200", "-n", "1"],
            TestContext.Current.CancellationToken
        );

        var login = harness.Transport.Written[0];
        Assert.StartsWith("user NZ0P pass -1 vers aprscli ", login, StringComparison.Ordinal);
        Assert.EndsWith(" filter r/35/-97/200", login, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecodedFormatPrintsReadableFields()
    {
        var harness = WithPackets(
            "N0CALL-9>APRS,WIDE1-1*,qAR,N1CALL-10:=4903.50N/07201.75W>088/036/A=001234 Hi",
            "N0CALL>APRS,TCPIP*:>status text",
            "N0CALL>APRS,TCPIP*:!9903.50N/07201.75W-"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "3"],
            TestContext.Current.CancellationToken
        );

        var lines = result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "N0CALL-9>APRS,WIDE1-1*,qAR,N1CALL-10  position 49.05833 -72.02917  /> Car  088° 36 kn  1234 ft  messaging  \"/A=001234 Hi\"",
            lines[0]
        );
        Assert.Equal("N0CALL>APRS,TCPIP*  status  \"status text\"", lines[1]);
        Assert.Equal(
            "N0CALL>APRS,TCPIP*  position (malformed)  !9903.50N/07201.75W-  [error InvalidLatitude at 20]",
            lines[2]
        );
    }

    [Fact]
    public async Task DecodedFormatShowsPositionExtensions()
    {
        var harness = WithPackets(
            "N0CALL>APRS:@092345z4903.5 N/07201.75W#PHG5132",
            "N0CALL>APRS:=4903.50N/07201.75W\\000/036/270/729",
            "N0CALL>APRS:/234517h4903.50N/07201.75W>DFS2360",
            "N0CALL>APRS:=/5L!!<*e7>{?!",
            "N0CALL>APRS:!0000.00N\\00000.00W.",
            "no header here"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "6"],
            TestContext.Current.CancellationToken
        );

        var lines = result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("(ambiguity 1)", lines[0], StringComparison.Ordinal);
        Assert.Contains("at 092345z", lines[0], StringComparison.Ordinal);
        Assert.Contains("PHG 25 W 20 ft 3 dBi 090° (7.9 mi)", lines[0], StringComparison.Ordinal);
        Assert.Contains("000° 36 kn", lines[1], StringComparison.Ordinal);
        Assert.Contains("DF bearing 270° quality 9", lines[1], StringComparison.Ordinal);
        Assert.Contains("DFS S2 80 ft 6 dBi omni", lines[2], StringComparison.Ordinal);
        Assert.Contains("range 20.1 mi", lines[3], StringComparison.Ordinal);
        Assert.Contains("position unknown", lines[4], StringComparison.Ordinal);
        Assert.Equal("  unknown (malformed)  no header here  [error MissingHeader at 0]", lines[5]);
    }

    [Fact]
    public async Task JsonFormatPrintsOneObjectPerLine()
    {
        var harness = WithPackets(
            "N0CALL-9>APRS,WIDE1-1*:@092345z4903.50N/07201.75W>088/036PHG5132 21.8°C!W23!",
            "N0CALL>APRS:=/5L!!<*e7>7P[",
            "N0CALL>APRS:=4903.50N/07201.75W\\000/036/270/729",
            "N0CALL>APRS:/234517h4903.50N/07201.75W>DFS2360",
            "no header here"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "json", "-t", "-n", "5"],
            TestContext.Current.CancellationToken
        );

        var lines = result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        using var first = System.Text.Json.JsonDocument.Parse(lines[0]);
        var root = first.RootElement;
        Assert.Equal("2026-10-11T12:00:00.123Z", root.GetProperty("received").GetString());
        Assert.Equal("N0CALL-9", root.GetProperty("source").GetString());
        Assert.Equal("WIDE1-1*", root.GetProperty("path")[0].GetString());
        Assert.Equal("position", root.GetProperty("type").GetString());
        Assert.Equal("parsed", root.GetProperty("outcome").GetString());
        var position = root.GetProperty("position");
        Assert.Equal(49.058367, position.GetProperty("latitude").GetDouble());
        Assert.Equal("Car", position.GetProperty("symbol").GetProperty("name").GetString());
        Assert.Equal("092345z", position.GetProperty("timestamp").GetProperty("sent").GetString());
        Assert.Equal("2026-10-09T23:45:00.000Z", position.GetProperty("timestamp").GetProperty("utc").GetString());
        Assert.Equal(88, position.GetProperty("course").GetInt32());
        Assert.False(position.TryGetProperty("phg", out _)); // one data extension per position: PHG stays in the comment
        Assert.Equal("W", position.GetProperty("datum").GetString());
        Assert.Equal("PHG5132 21.8°C", position.GetProperty("comment").GetString());
        Assert.Contains("21.8°C", lines[0], StringComparison.Ordinal);

        using var compressed = System.Text.Json.JsonDocument.Parse(lines[1]);
        var type = compressed.RootElement.GetProperty("position").GetProperty("compressionType");
        Assert.Equal("current", type.GetProperty("gpsFix").GetString());
        Assert.Equal("rmc", type.GetProperty("nmeaSource").GetString());
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            compressed.RootElement.GetProperty("position").GetProperty("altitudeFeet").ValueKind
        );

        using var df = System.Text.Json.JsonDocument.Parse(lines[2]);
        Assert.True(df.RootElement.GetProperty("position").GetProperty("df").GetProperty("fixedStation").GetBoolean());
        using var dfs = System.Text.Json.JsonDocument.Parse(lines[3]);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            dfs.RootElement.GetProperty("position").GetProperty("dfs").GetProperty("directivity").ValueKind
        );

        using var bad = System.Text.Json.JsonDocument.Parse(lines[4]);
        Assert.Equal("malformed", bad.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("missingHeader", bad.RootElement.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.False(bad.RootElement.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task StatusReportsPrintDecodedAndAsJson()
    {
        string[] packets =
        [
            "N0CALL>APRS:>092345zNet Control",
            "N0CALL>APRS:>IO91SX/- ^B7",
            // The client hands over one character per byte, so UTF-8 text arrives as Latin-1 characters.
            "N0CALL>APRS:>" + Encoding.Latin1.GetString("Ciallo ☆"u8),
        ];

        var decoded = await WithPackets(packets)
            .RunAsync(
                ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "3"],
                TestContext.Current.CancellationToken
            );
        var json = await WithPackets(packets)
            .RunAsync(["listen", "-c", "NZ0P", "--format", "json", "-n", "3"], TestContext.Current.CancellationToken);

        var lines = decoded.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("N0CALL>APRS  status  at 092345z  \"Net Control\"", lines[0]);
        Assert.Equal("N0CALL>APRS  status  locator IO91SX  /- House (VHF)  beam 110° 490 W ERP  \"^B7\"", lines[1]);
        Assert.Equal("N0CALL>APRS  status  \"Ciallo ☆\"", lines[2]);

        var objects = json
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("status"))
            .ToList();
        Assert.Equal("Net Control", objects[0].GetProperty("text").GetString());
        Assert.Equal("2026-10-09T23:45:00.000Z", objects[0].GetProperty("timestamp").GetProperty("utc").GetString());
        Assert.Equal("IO91SX", objects[1].GetProperty("locator").GetString());
        Assert.Equal("House (VHF)", objects[1].GetProperty("symbol").GetProperty("name").GetString());
        Assert.Equal(110, objects[1].GetProperty("beamHeading").GetInt32());
        Assert.Equal(490, objects[1].GetProperty("erpWatts").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, objects[2].GetProperty("beamHeading").ValueKind);
        Assert.False(objects[2].TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task PrettyFormatPrintsALabelledBlockPerPacket()
    {
        var harness = WithPackets(
            "N0CALL-9>APRS,WIDE1-1*,qAR,N1CALL-10:@092345z4903.5 N/07201.75W>088/036/A=001234 Hi",
            "N0CALL>APRS:>IO91SX/- ^B7",
            "N0CALL>APRS:$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A",
            "N0CALL>APRS:!3907.62n/09447.19w#PHG5132",
            "no header here"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "pretty", "-t", "-n", "5"],
            TestContext.Current.CancellationToken
        );

        var blocks = result.Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, blocks.Length);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "12:00:00  N0CALL-9 → APRS  via WIDE1-1*,qAR,N1CALL-10",
                "          position  49.05833  -72.02833  (ambiguous to 0.1 minute)",
                "          symbol    />  Car",
                "          time      9 Oct 23:45 UTC  (092345z)",
                "          course    088° at 36 kn (66.7 km/h)",
                "          altitude  1,234 ft (376 m)",
                "          messaging yes",
                "          comment   \"/A=001234 Hi\""
            ),
            blocks[0]
        );
        Assert.Contains("status    \"^B7\"", blocks[1], StringComparison.Ordinal);
        Assert.Contains("locator   IO91SX", blocks[1], StringComparison.Ordinal);
        Assert.Contains("beam      110°, 490 W ERP", blocks[1], StringComparison.Ordinal);
        Assert.Contains("unknown   not decoded yet", blocks[2], StringComparison.Ordinal);
        Assert.Contains("data      $GPRMC,123519", blocks[2], StringComparison.Ordinal);
        Assert.Contains(
            "station   25 W, antenna 20 ft, 3 dBi toward 090°, range 7.9 mi",
            blocks[3],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "warning   A position uses a lower-case hemisphere letter. (byte 20)",
            blocks[3],
            StringComparison.Ordinal
        );
        Assert.Contains("not a packet", blocks[4], StringComparison.Ordinal);
        Assert.Contains("error     No SOURCE>DESTINATION header", blocks[4], StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrettyFormatShowsEveryPositionField()
    {
        var harness = WithPackets(
            "N0CALL>APRS:=/5L!!<*e7>7P[",
            "N0CALL>APRS:=4903.50N/07201.75W\\000/036/270/729",
            "N0CALL>APRS:/234517h4903.50N/07201.75W>DFS2360",
            "N0CALL>APRS:=/5L!!<*e7>{?!",
            "N0CALL>APRS:!0000.00N\\00000.00W.",
            "N0CALL>APRS:@092345/4903.50N/07201.75W_220/004g005!w0x!",
            "N0CALL>APRS:!4903.50N/07201.75W-\u001b[31mred",
            "N0CALL>APRS:!4903.50N/07201.75W-!N12!",
            "N0CALL>APRS:=4903.50N/07201.75W>.../036"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "9"],
            TestContext.Current.CancellationToken
        );

        var blocks = result.Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("GPS       current fix, RMC, software", blocks[0], StringComparison.Ordinal);
        Assert.Contains("bearing   270°, quality 9, 7 hits, 4 mi, fixed station", blocks[1], StringComparison.Ordinal);
        Assert.Contains("DF        signal S2, antenna 80 ft, 6 dBi omni", blocks[2], StringComparison.Ordinal);
        Assert.Contains("range     20.1 mi", blocks[3], StringComparison.Ordinal);
        Assert.Contains("position  unknown", blocks[4], StringComparison.Ordinal);
        Assert.Contains("time      092345/ (sender's local time)", blocks[5], StringComparison.Ordinal);
        Assert.Contains("wind      220° 4 mph, gusts 5 mph", blocks[5], StringComparison.Ordinal);
        Assert.Contains("datum     WGS84 (extra precision)", blocks[5], StringComparison.Ordinal);
        Assert.Contains("comment   \"\\x1B[31mred\"", blocks[6], StringComparison.Ordinal);
        Assert.Contains("datum     N (extra precision)", blocks[7], StringComparison.Ordinal);
        Assert.Contains("course    course unknown at 36 kn", blocks[8], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("always", false, null, null, true)]
    [InlineData("never", true, null, null, false)]
    [InlineData("auto", true, null, null, true)]
    [InlineData("auto", false, null, null, false)]
    [InlineData("auto", true, "1", null, false)]
    [InlineData("auto", true, null, "dumb", false)]
    public async Task ColourFollowsTheColorOption(
        string color,
        bool terminal,
        string? noColor,
        string? term,
        bool coloured
    )
    {
        var harness = WithPackets("N0CALL>APRS:!4903.50N/07201.75W-");
        harness.OutIsTerminal = terminal;
        if (noColor is not null)
        {
            harness.Environment["NO_COLOR"] = noColor;
        }

        if (term is not null)
        {
            harness.Environment["TERM"] = term;
        }

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--format", "pretty", "--color", color, "-n", "1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(coloured, result.Out.Contains('\u001b', StringComparison.Ordinal));
    }

    [Fact]
    public async Task MessagesPrintInEveryFormat()
    {
        string[] packets =
        [
            "N0CALL>APRS::WU2Z     :Testing{003}AB",
            "N0CALL>APRS::KB2ICI-14:ack003",
            "N0CALL>APRS::KB2ICI-14:rej004",
            "N0CALL>APRS::BLN4WX   :Stand by",
            "N0CALL>APRS::BLNQ     :QRT",
            "N0CALL>APRS::NWS-WARN :Storm{S9JbA",
            "N0CALL>APRS::KH2Z     :?APRSH N0QBF",
            "N0QBF>APRS::N0QBF-11 :EQNS.0,5.2,0,0,.53,-32",
            "N0QBF>APRS::N0QBF-11 :BITS.10110000,Balloon",
            "N0QBF>APRS::N0QBF-11 :PARM.Battery,Btemp",
        ];

        var decoded = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "10"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("N0CALL>APRS  message to WU2Z  #003  reply-ack AB  \"Testing\"", decoded[0]);
        Assert.Equal("N0CALL>APRS  ack to KB2ICI-14 for 003", decoded[1]);
        Assert.Equal("N0CALL>APRS  reject to KB2ICI-14 for 004", decoded[2]);
        Assert.Equal("N0CALL>APRS  bulletin 4 to group WX  \"Stand by\"", decoded[3]);
        Assert.Equal("N0CALL>APRS  announcement Q  \"QRT\"", decoded[4]);
        Assert.Equal("N0CALL>APRS  NWS bulletin NWS-WARN  #S9JbA  \"Storm\"", decoded[5]);
        Assert.Equal("N0CALL>APRS  query APRSH to KH2Z  N0QBF", decoded[6]);
        Assert.Equal(
            "N0QBF>APRS  telemetry equations for N0QBF-11  0·v² + 5.2·v + 0 | 0·v² + 0.53·v + -32",
            decoded[7]
        );
        Assert.Equal("N0QBF>APRS  telemetry bit sense for N0QBF-11  10110000 \"Balloon\"", decoded[8]);
        Assert.Equal("N0QBF>APRS  telemetry names for N0QBF-11  Battery, Btemp", decoded[9]);

        var json = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "json", "-n", "10"],
                    TestContext.Current.CancellationToken
                )
        )
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .ToList();
        var message = json[0].GetProperty("message");
        Assert.Equal("WU2Z", message.GetProperty("addressee").GetString());
        Assert.Equal("message", message.GetProperty("kind").GetString());
        Assert.Equal("003", message.GetProperty("messageId").GetString());
        Assert.Equal("AB", message.GetProperty("replyAck").GetString());
        Assert.True(message.GetProperty("ackRequired").GetBoolean());
        Assert.Equal("acknowledgement", json[1].GetProperty("message").GetProperty("kind").GetString());
        Assert.Equal("WX", json[3].GetProperty("message").GetProperty("bulletinGroup").GetString());
        Assert.Equal("N0QBF", json[6].GetProperty("message").GetProperty("queryArgument").GetString());
        var equations = json[7].GetProperty("telemetryDefinition");
        Assert.Equal("equations", equations.GetProperty("kind").GetString());
        Assert.Equal(-32, equations.GetProperty("equations")[1].GetProperty("c").GetDouble());
        Assert.Equal("Balloon", json[8].GetProperty("telemetryDefinition").GetProperty("projectName").GetString());
        Assert.Equal("Btemp", json[9].GetProperty("telemetryDefinition").GetProperty("labels")[1].GetString());

        var pretty = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "10"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("to        WU2Z", pretty[0], StringComparison.Ordinal);
        Assert.Contains("id        003 (ack expected)", pretty[0], StringComparison.Ordinal);
        Assert.Contains("reply-ack also acknowledges AB", pretty[0], StringComparison.Ordinal);
        Assert.Contains("ack       003 to KB2ICI-14", pretty[1], StringComparison.Ordinal);
        Assert.Contains("reject    004 to KB2ICI-14", pretty[2], StringComparison.Ordinal);
        Assert.Contains("bulletin  4 to group WX", pretty[3], StringComparison.Ordinal);
        Assert.Contains("announce  Q", pretty[4], StringComparison.Ordinal);
        Assert.Contains("NWS       NWS-WARN", pretty[5], StringComparison.Ordinal);
        Assert.Contains("query     APRSH to KH2Z", pretty[6], StringComparison.Ordinal);
        Assert.Contains("about     N0QBF", pretty[6], StringComparison.Ordinal);
        Assert.Contains("telemetry equations for N0QBF-11", pretty[7], StringComparison.Ordinal);
        Assert.Contains("equations 0·v² + 5.2·v + 0", pretty[7], StringComparison.Ordinal);
        Assert.Contains("values    10110000 \"Balloon\"", pretty[8], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObjectsAndItemsPrintInEveryFormat()
    {
        string[] packets =
        [
            "N0CALL>APRS:;LEADER   _092345z4903.50N/07201.75W>088/036",
            "N0CALL>APRS:)AID #2!4903.50N/07201.75WA",
            "N0CALL>APRS:;ROUTE    *092345z4903.50N\\07201.75Wl610/310{100}",
            "N0CALL>APRS:)I91 3N!4903.50N\\07201.75Wm{55}",
        ];

        var decoded = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "4"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith(
            "N0CALL>APRS  object LEADER (killed)  position 49.05833 -72.02917  /> Car  at 092345z",
            decoded[0],
            StringComparison.Ordinal
        );
        Assert.StartsWith("N0CALL>APRS  item AID #2  position 49.05833", decoded[1], StringComparison.Ordinal);
        Assert.Contains("area line, colour 3, 0.067° × 0.067°, corridor 100 mi", decoded[2], StringComparison.Ordinal);
        Assert.Contains("signpost 55", decoded[3], StringComparison.Ordinal);
        Assert.DoesNotContain("messaging", decoded[0], StringComparison.Ordinal);

        var json = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "json", "-n", "4"],
                    TestContext.Current.CancellationToken
                )
        )
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .ToList();
        var obj = json[0].GetProperty("object");
        Assert.Equal("LEADER", obj.GetProperty("name").GetString());
        Assert.False(obj.GetProperty("live").GetBoolean());
        Assert.False(obj.TryGetProperty("messaging", out _));
        Assert.Equal("AID #2", json[1].GetProperty("item").GetProperty("name").GetString());
        var area = json[2].GetProperty("object").GetProperty("area");
        Assert.Equal("line", area.GetProperty("shape").GetString());
        Assert.Equal(100, area.GetProperty("corridorMiles").GetInt32());
        Assert.Equal("55", json[3].GetProperty("item").GetProperty("signpost").GetString());

        var pretty = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "4"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("object    LEADER  killed", pretty[0], StringComparison.Ordinal);
        Assert.DoesNotContain("messaging", pretty[0], StringComparison.Ordinal);
        Assert.Contains("item      AID #2", pretty[1], StringComparison.Ordinal);
        Assert.Contains("area      line, colour 3", pretty[2], StringComparison.Ordinal);
        Assert.Contains("signpost  55", pretty[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MicEReportsPrintInEveryFormat()
    {
        string[] packets = ["N0CALL>T4SQZZ:`(_fn\"Oj/`\"4T}On the road _%", "N0CALL>000PPP:'(_fn\"Oj/'Help!"];

        var decoded = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("N0CALL>T4SQZZ  Mic-E position 44.51667 -112.11667", decoded[0], StringComparison.Ordinal);
        Assert.Contains("In Service  FTM-400DR  messaging", decoded[0], StringComparison.Ordinal);
        Assert.Contains("Emergency", decoded[1], StringComparison.Ordinal);
        Assert.DoesNotContain("messaging", decoded[1], StringComparison.Ordinal);

        var json = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "json", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        )
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("micE"))
            .ToList();
        Assert.Equal("inService", json[0].GetProperty("message").GetString());
        Assert.Equal("FTM-400DR", json[0].GetProperty("device").GetString());
        Assert.True(json[0].GetProperty("currentFix").GetBoolean());
        Assert.True(json[0].GetProperty("messaging").GetBoolean());
        Assert.Equal(200.1, json[0].GetProperty("altitudeFeet").GetDouble(), 1);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json[1].GetProperty("device").ValueKind);

        var pretty = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Mic-E     In Service", pretty[0], StringComparison.Ordinal);
        Assert.Contains("device    FTM-400DR", pretty[0], StringComparison.Ordinal);
        Assert.Contains("fix       current", pretty[0], StringComparison.Ordinal);
        Assert.Contains("Mic-E     Emergency", pretty[1], StringComparison.Ordinal);
        Assert.Contains("device    not identified", pretty[1], StringComparison.Ordinal);
        Assert.Contains("fix       old", pretty[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TelemetryReportsPrintInEveryFormat()
    {
        string[] packets = ["N0CALL>APRS:T#005,199,000,255,073,123,01101001Solar", "N0CALL>APRS:T#MIC13.35,,0.5"];

        var decoded = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "N0CALL>APRS  telemetry #005  199, 0, 255, 73, 123  bits 01101001  \"Solar\"",
                "N0CALL>APRS  telemetry #MIC  13.35, -, 0.5",
            ],
            decoded
        );

        var json = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "json", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        )
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("telemetry"))
            .ToList();
        Assert.Equal(5, json[0].GetProperty("sequenceNumber").GetInt32());
        Assert.Equal(73, json[0].GetProperty("analog")[3].GetDouble());
        Assert.Equal("01101001", json[0].GetProperty("digital").GetString());
        Assert.Equal("Solar", json[0].GetProperty("comment").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json[1].GetProperty("sequenceNumber").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json[1].GetProperty("analog")[1].ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json[1].GetProperty("digital").ValueKind);

        var pretty = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("telemetry #005", pretty[0], StringComparison.Ordinal);
        Assert.Contains("analog    199, 0, 255, 73, 123", pretty[0], StringComparison.Ordinal);
        Assert.Contains("digital   01101001", pretty[0], StringComparison.Ordinal);
        Assert.Contains("comment   \"Solar\"", pretty[0], StringComparison.Ordinal);
        Assert.DoesNotContain("digital", pretty[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeatherReportsPrintInEveryFormat()
    {
        string[] packets =
        [
            "N0CALL>APRS:_10090556c220s004g005t077r001p002P003h50b09900wRSW",
            "N0CALL>APRS:!4903.50N/07201.75W_.../...t-07h100b.....L850AmbientCWOP.com",
        ];

        var decoded = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "decoded", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "N0CALL>APRS  weather at 10090556  wind 220° 4 mph, gusts 5 mph  temp 77°F (25.0°C)  "
                    + "rain 0.01 in last hour, 0.02 in 24 h, 0.03 in since midnight  humidity 50%  pressure 990.0 mb  \"wRSW\"",
                "N0CALL>APRS  position 49.05833 -72.02917  /_ Weather station (blue)  temp -7°F (-21.7°C)  humidity 100%  "
                    + "sunlight 850 W/m²  \"AmbientCWOP.com\"",
            ],
            decoded
        );

        var json = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "json", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        )
            .Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .ToList();
        var positionless = json[0].GetProperty("weather");
        Assert.Equal("10090556", positionless.GetProperty("timestamp").GetProperty("sent").GetString());
        Assert.Equal(220, positionless.GetProperty("windDirection").GetInt32());
        Assert.Equal(0.03, positionless.GetProperty("rainSinceMidnightInches").GetDouble());
        Assert.Equal("wRSW", positionless.GetProperty("comment").GetString());
        var station = json[1].GetProperty("position").GetProperty("weather");
        Assert.Equal(-7, station.GetProperty("temperatureF").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, station.GetProperty("pressureMillibars").ValueKind);
        Assert.Equal("AmbientCWOP.com", json[1].GetProperty("position").GetProperty("comment").GetString());

        var pretty = (
            await WithPackets(packets)
                .RunAsync(
                    ["listen", "-c", "NZ0P", "--format", "pretty", "-n", "2"],
                    TestContext.Current.CancellationToken
                )
        ).Out.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("weather   9 Oct 05:56 UTC  (10090556)", pretty[0], StringComparison.Ordinal);
        Assert.Contains("wind      220° 4 mph, gusts 5 mph", pretty[0], StringComparison.Ordinal);
        Assert.Contains("pressure  990.0 mb", pretty[0], StringComparison.Ordinal);
        Assert.Contains("comment   \"wRSW\"", pretty[0], StringComparison.Ordinal);
        Assert.Contains("temp      -7°F (-21.7°C)", pretty[1], StringComparison.Ordinal);
        Assert.Contains("sunlight  850 W/m²", pretty[1], StringComparison.Ordinal);
        Assert.DoesNotContain("wind", pretty[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureStaysRawWhateverTheFormat()
    {
        var capture = Path.Combine(Path.GetTempPath(), $"aprscli-{Guid.NewGuid():N}.log");
        try
        {
            var harness = WithPackets("N0CALL>APRS:!4903.50N/07201.75W-");

            await harness.RunAsync(
                ["listen", "-c", "NZ0P", "--format", "json", "--capture", capture, "-n", "1"],
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                "2026-10-11T12:00:00.123Z N0CALL>APRS:!4903.50N/07201.75W-\n",
                await File.ReadAllTextAsync(capture, TestContext.Current.CancellationToken)
            );
        }
        finally
        {
            File.Delete(capture);
        }
    }

    [Fact]
    public async Task TimestampsFlagPrefixesConsoleLines()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-t", "-n", "1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal($"2026-10-11T12:00:00.123Z {_packets[0]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task CaptureAppendsTimestampedLinesEvenWithoutTheTimestampsFlag()
    {
        var harness = WithPackets(_packets);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "nested", "cap.log");

        var first = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--capture", file, "-n", "2"],
            TestContext.Current.CancellationToken
        );
        var second = await new Func<Task<CliResult>>(async () =>
        {
            var again = WithPackets(_packets[2]);
            return await again.RunAsync(["listen", "-c", "NZ0P", "--capture", file, "-n", "1"]);
        })();

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(
            [
                $"2026-10-11T12:00:00.123Z {_packets[0]}",
                $"2026-10-11T12:00:00.123Z {_packets[1]}",
                $"2026-10-11T12:00:00.123Z {_packets[2]}",
            ],
            await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)
        );
        Assert.DoesNotContain("2026-", first.Out, StringComparison.Ordinal); // console still untimestamped
    }

    [Fact]
    public async Task QuietSilencesTheConsoleButNotTheCapture()
    {
        var harness = WithPackets(_packets);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "cap.log");

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-q", "--capture", file, "-n", "3"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Out);
        Assert.Equal(3, (await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task ConsoleEscapesControlCharactersButTheCaptureKeepsEveryByte()
    {
        var raw = "A>B:>\u001b[31mred\u0000\u001cé";
        var harness = WithPackets(raw);
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "cap.log");

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--capture", file, "-n", "1"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal("A>B:>\\x1B[31mred\\x00\\x1Cé" + Environment.NewLine, result.Out);
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        var expected = Encoding.Latin1.GetBytes("2026-10-11T12:00:00.123Z " + raw + "\n");
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task ServerCommentsAreNotPrintedOrCounted()
    {
        var harness = WithPackets("# keepalive", _packets[0], "# another", _packets[1]);

        var result = await harness.RunAsync(["listen", "-c", "NZ0P", "-n", "2"], TestContext.Current.CancellationToken);

        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task DurationStopsAQuietFeedCleanly()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.2s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task CtrlCStopsCleanly()
    {
        var harness = new CliHarness().LoginAccepted();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], cts.Token);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ServerClosingTheConnectionIsAConnectionFailure()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}", result.Out);
    }

    [Fact]
    public async Task ConnectionErrorMidStreamIsAConnectionFailure()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new IOException("reset"));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("connection lost", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASilentServerIsAConnectionFailureWithoutReconnect()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new TimeoutException("No data from the server for 90s."));

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("connection lost: No data from the server for 90s.", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectResumesAfterTheServerClosesAndCountSpansConnections()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();
        harness.AddReconnect(_packets[1], _packets[2]);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
        Assert.Contains("closed the connection", result.Error, StringComparison.Ordinal);
        Assert.Contains("reconnecting to rotate.aprs2.net:14580 in 0s", result.Error, StringComparison.Ordinal);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectResumesAfterTheServerGoesSilent()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Fail(new TimeoutException("No data from the server for 90s."));
        harness.AddReconnect(_packets[1]);

        using var capture = new TempDirectory();
        var file = Path.Combine(capture.Path, "c.log");
        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-n", "2", "--capture", file],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No data from the server", result.Error, StringComparison.Ordinal);
        var captured = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);
        Assert.Equal(2, captured.Length);
        Assert.EndsWith(_packets[1], captured[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconnectKeepsRetryingAnUnreachableServerUntilTheDurationEnds()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "1s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.ConnectionAttempts >= 3, $"only {harness.ConnectionAttempts} attempts");
        Assert.Contains("could not connect", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconnectDoesNotRetryAFailedFirstConnection()
    {
        var harness = new CliHarness { ConnectFailure = SocketFailures.Refused() };

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "1s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task ReconnectStopsOnARejectedLogin()
    {
        var harness = WithPackets(_packets[0]);
        harness.Transport.Close();
        var second = new FakeAprsTransport();
        second.Server("# Login by user not allowed");
        harness.Reconnects.Enqueue(second);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "-d", "5s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task MaxSilenceReconnectsWhenOnlyKeepalivesArrive()
    {
        var harness = WithPackets(_packets[0]);
        harness.AddReconnect(_packets[1]);

        var run = harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "--max-silence", "2s", "-n", "2"],
            TestContext.Current.CancellationToken
        );
        await Task.Delay(150, TestContext.Current.CancellationToken);
        harness.Transport.Server("# aprsc keepalive");
        harness.Transport.Server("# aprsc keepalive");
        var result = await run;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "connection lost: No packets from T2TEST for 2s (server keepalives in that time: 2).",
            result.Error,
            StringComparison.Ordinal
        );
        Assert.Equal($"{_packets[0]}{Environment.NewLine}{_packets[1]}{Environment.NewLine}", result.Out);
        Assert.Equal(2, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task MaxSilenceWithoutReconnectIsAConnectionFailure()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--max-silence", "0.2s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("connection lost: No packets from T2TEST for 0.2s", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaxSilenceOffDisablesTheReconnectDefault()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--reconnect", "--max-silence", "off", "-d", "0.3s"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task AnInvalidMaxSilenceIsAUsageError()
    {
        var harness = new CliHarness().LoginAccepted();

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--max-silence", "soon"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--max-silence 'soon' is not valid", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, harness.ConnectionAttempts);
    }

    [Fact]
    public async Task UnreachableServerIsAConnectionFailure()
    {
        var harness = new CliHarness { ConnectFailure = SocketFailures.Refused() };

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--server", "127.0.0.1:9"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("could not connect to 127.0.0.1:9", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedDefaultCallsignExplainsAndExits4()
    {
        var harness = new CliHarness();
        harness.Transport.Server("# aprsc 2.1.21-gbfc2090");
        harness.Transport.Server("# Login by user not allowed");

        var result = await harness.RunAsync(["listen"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("Login by user not allowed", result.Error, StringComparison.Ordinal);
        Assert.Contains("placeholder callsign N0CALL", result.Error, StringComparison.Ordinal);
        Assert.Contains("--callsign", result.Error, StringComparison.Ordinal);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public async Task RejectedCustomCallsignGetsNoPlaceholderHint()
    {
        var harness = new CliHarness();
        harness.Transport.Server("# Login by user not allowed");

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.DoesNotContain("placeholder", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginClosedBeforeAnyAnswerExits4()
    {
        var harness = new CliHarness();
        harness.Transport.Close();

        var result = await harness.RunAsync(["listen", "-c", "NZ0P"], TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task DefaultsAreN0callReadOnlyOnTheWorldwideRotate()
    {
        var harness = new CliHarness().LoginAccepted("N0CALL");

        await harness.RunAsync(["listen", "-d", "0.1s"], TestContext.Current.CancellationToken);

        Assert.Equal("N0CALL", harness.ClientOptions!.Callsign);
        Assert.Equal(-1, harness.ClientOptions.Passcode);
        Assert.Equal("rotate.aprs2.net:14580", harness.ClientOptions.Endpoint.ToString());
    }

    [Theory]
    [InlineData("--region noam", "noam.aprs2.net:14580")]
    [InlineData("--region euro --full-feed", "euro.aprs2.net:10152")]
    [InlineData("--region asia --port 20152", "asia.aprs2.net:20152")]
    [InlineData("--server example.org", "example.org:14580")]
    [InlineData("--server example.org:1234", "example.org:1234")]
    [InlineData("--server example.org --full-feed", "example.org:10152")]
    [InlineData("--server example.org --port 99", "example.org:99")]
    [InlineData("--full-feed", "rotate.aprs2.net:10152")]
    public async Task ResolvesTheEndpoint(string args, string expected)
    {
        var harness = new CliHarness().LoginAccepted();

        await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.1s", .. args.Split(' ')],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, harness.ClientOptions!.Endpoint.ToString());
    }

    [Fact]
    public async Task PasscodeComesFromTheEnvironmentAndTheOptionWins()
    {
        var fromEnvironment = new CliHarness().LoginAccepted();
        fromEnvironment.Environment["APRS_PASSCODE"] = "12345";
        var envResult = await fromEnvironment.RunAsync(
            ["listen", "-c", "NZ0P", "-d", "0.1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(12345, fromEnvironment.ClientOptions!.Passcode);

        var optionWins = new CliHarness().LoginAccepted();
        optionWins.Environment["APRS_PASSCODE"] = "12345";
        var optionResult = await optionWins.RunAsync(
            ["listen", "-c", "NZ0P", "--passcode", "777", "-d", "0.1s"],
            TestContext.Current.CancellationToken
        );
        Assert.Equal(777, optionWins.ClientOptions!.Passcode);

        foreach (var text in new[] { envResult.Out, envResult.Error, optionResult.Out, optionResult.Error })
        {
            Assert.DoesNotContain("12345", text, StringComparison.Ordinal);
            Assert.DoesNotContain("777", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("listen --server a.example --region noam", "cannot be used together")]
    [InlineData("listen --port 14580 --full-feed", "--port and --full-feed")]
    [InlineData("listen --server a.example:1234 --port 99", "already includes a port")]
    [InlineData("listen --server a.example:1234 --full-feed", "already includes a port")]
    [InlineData("listen --port 0", "between 1 and 65535")]
    [InlineData("listen --port 70000", "between 1 and 65535")]
    [InlineData("listen --server a:b:c", "not valid")]
    [InlineData("listen --duration soon", "not valid")]
    [InlineData("listen --duration 0s", "not valid")]
    [InlineData("listen --duration -5m", "not valid")]
    [InlineData("listen --count 0", "at least 1")]
    [InlineData("listen --quiet", "silently")]
    [InlineData("listen --capture", "")]
    [InlineData("listen --callsign TOOLONGCALL1", "Callsign")]
    [InlineData("listen --callsign N0CALL --filter \"a\\nb\"", "")]
    [InlineData("listen --region mars", "")]
    [InlineData("listen -c NZ0P --format yaml", "")]
    [InlineData("listen -c NZ0P --color sometimes", "")]
    [InlineData("listen --bogus", "")]
    [InlineData("nope", "")]
    [InlineData("", "")]
    public async Task UsageErrorsExit3WithoutConnecting(string commandLine, string expectedText)
    {
        var harness = new CliHarness().LoginAccepted();
        var args =
            commandLine.Length == 0
                ? []
                : commandLine
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Replace("\\n", "\n", StringComparison.Ordinal))
                    .ToArray();

        var result = await harness.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(expectedText, result.Error, StringComparison.Ordinal);
        Assert.Null(harness.ClientOptions);
        Assert.False(harness.Transport.Connected);
    }

    [Fact]
    public async Task ANonNumericPasscodeVariableIsAUsageErrorAndIsNotEchoed()
    {
        var harness = new CliHarness().LoginAccepted();
        harness.Environment["APRS_PASSCODE"] = "hunter2";

        var result = await harness.RunAsync(["listen"], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("APRS_PASSCODE must be an integer", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PacketsAreNotLoggedByDefaultEvenWithAnEndpoint()
    {
        var harness = WithPackets(_packets);
        harness.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";

        var result = await harness.RunAsync(["listen", "-c", "NZ0P", "-n", "3"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(harness.Logs.Entries, e => e.Category == "aprscli.packets");
    }

    [Fact]
    public async Task LogPacketsLogsEachPacketToThePacketsCategory()
    {
        var harness = WithPackets("W1AW>APRS,TCPIP*:>one", "# comment", "K1ABC>APRS:>\u001b[31mtwo");
        harness.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--log-packets", "-n", "2"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        var logged = harness.Logs.Entries.Where(e => e.Category == "aprscli.packets").ToList();
        Assert.Equal(
            [
                "Packet from W1AW: W1AW>APRS,TCPIP*:>one",
                "Packet from K1ABC: K1ABC>APRS:>\\x1B[31mtwo", // control characters escaped for the backend
            ],
            logged.Select(e => e.Message)
        );
        Assert.All(logged, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Contains("W1AW", result.Out, StringComparison.Ordinal); // console output is unaffected
    }

    [Fact]
    public async Task LogPacketsWithoutAnOtlpEndpointIsAUsageError()
    {
        var harness = WithPackets(_packets);

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "--log-packets"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", result.Error, StringComparison.Ordinal);
        Assert.False(harness.Transport.Connected);
    }

    [Fact]
    public async Task HelpSucceeds()
    {
        var result = await new CliHarness().RunAsync(["listen", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--timestamps", result.Out, StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aprscli-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }

    [Fact]
    public async Task StatsSummarizeThePacketsAtTheEnd()
    {
        var harness = WithPackets(
            "W1AW>APRS:>one",
            "W1AW>APRS:!4903.50N/07201.75W>",
            "W1AW>APRS:>two",
            "W1AW>APRS:!9903.50N/07201.75W>",
            "WU2Z>APRS::NZ0P     :Hello{1",
            "WU2Z>APRS::NZ0P-9   :Not for this SSID{2",
            "WU2Z>APRS:%not a known type"
        );

        var result = await harness.RunAsync(
            ["listen", "-c", "NZ0P", "-n", "7", "--stats", "1h", "-q"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "7 packets (1 malformed, 1 unsupported, 1 to NZ0P); position 2, message 2, status 2, unknown 1",
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task WithoutStatsNoSummaryIsPrinted()
    {
        var harness = WithPackets(_packets[0]);

        var result = await harness.RunAsync(["listen", "-c", "NZ0P", "-n", "1"], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("packets (", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidStatsIntervalIsAUsageError()
    {
        var result = await new CliHarness().RunAsync(
            ["listen", "-c", "NZ0P", "--stats", "often"],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("--stats 'often' is not valid", result.Error, StringComparison.Ordinal);
    }
}
