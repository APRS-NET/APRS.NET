using System.Globalization;
using System.Text;
using System.Text.Json;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>Parser statistics over a set of captured lines, for <c>aprscli replay</c>.</summary>
internal sealed class ReplayReport(int examples)
{
    private readonly Dictionary<ParseOutcome, int> _outcomes = [];
    private readonly SortedDictionary<PacketType, int[]> _types = [];
    private readonly Dictionary<(ParseIssueCode Code, ParseIssueSeverity Severity), Group> _issues = [];
    private readonly Dictionary<string, Group> _undecoded = new(StringComparer.Ordinal);

    public int Files { get; set; }

    public int Lines { get; private set; }

    public int ServerComments { get; private set; }

    public int BlankLines { get; private set; }

    public int Packets { get; private set; }

    /// <summary>Counts one input line. <paramref name="location"/> (file:line) is kept with examples.</summary>
    public void Add(string line, string location)
    {
        Lines++;
        if (line.Length == 0)
        {
            BlankLines++;
            return;
        }

        if (line[0] == '#')
        {
            ServerComments++; // APRS-IS keepalives and logresp lines are not packets
            return;
        }

        var packet = AprsPacket.Parse(line);
        Packets++;
        _outcomes[packet.Outcome] = _outcomes.GetValueOrDefault(packet.Outcome) + 1;

        if (!_types.TryGetValue(packet.Type, out var row))
        {
            _types[packet.Type] = row = new int[3];
        }

        row[(int)packet.Outcome]++;

        foreach (var key in packet.Issues.Select(i => (i.Code, i.Severity)).Distinct())
        {
            if (!_issues.TryGetValue(key, out var group))
            {
                _issues[key] = group = new Group();
            }

            group.Add(line, location, examples);
        }

        if (packet.Outcome == ParseOutcome.Unsupported || (packet.HasHeader && packet.Type == PacketType.Unknown))
        {
            var identifier = Identifier(packet);
            if (!_undecoded.TryGetValue(identifier, out var group))
            {
                _undecoded[identifier] = group = new Group();
            }

            group.Add(line, location, examples);
        }
    }

    public string ToText()
    {
        var text = new StringBuilder();
        var skipped = new List<string>();
        if (ServerComments > 0)
        {
            skipped.Add(Count(ServerComments, "server comment"));
        }

        if (BlankLines > 0)
        {
            skipped.Add(Count(BlankLines, "blank line"));
        }

        text.Append(
                CultureInfo.InvariantCulture,
                $"{Count(Packets, "packet")} from {Count(Lines, "line")} in {Count(Files, "input")}"
            )
            .AppendLine(skipped.Count > 0 ? $" ({string.Join(" and ", skipped)} skipped)" : "");
        text.AppendLine();

        foreach (var outcome in Enum.GetValues<ParseOutcome>())
        {
            var count = _outcomes.GetValueOrDefault(outcome);
            text.AppendLine(CultureInfo.InvariantCulture, $"{outcome, -12} {N(count), 9}  {Percent(count), 6}");
        }

        if (_types.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(
                CultureInfo.InvariantCulture,
                $"{"Type", -12} {"Packets", 9} {"Parsed", 9} {"Unsupported", 12} {"Malformed", 10}"
            );
            foreach (var (type, row) in _types)
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"{type, -12} {N(row.Sum()), 9} {N(row[0]), 9} {N(row[1]), 12} {N(row[2]), 10}"
                );
            }
        }

        if (_issues.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Issues (packets with each issue)");
            foreach (var ((code, severity), group) in Ordered(_issues))
            {
                text.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  {code, -28} {Severity(severity), -7} {N(group.Count), 7}  {code.Describe()}"
                );
                AppendExamples(text, group);
            }
        }

        if (_undecoded.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Not decoded yet, by data type identifier");
            foreach (var (identifier, group) in Ordered(_undecoded))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {identifier, -28} {N(group.Count), 15}");
                AppendExamples(text, group);
            }
        }

        return text.ToString();
    }

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("inputs", Files);
            json.WriteNumber("lines", Lines);
            json.WriteNumber("serverComments", ServerComments);
            json.WriteNumber("blankLines", BlankLines);
            json.WriteNumber("packets", Packets);

            json.WriteStartObject("outcomes");
            foreach (var outcome in Enum.GetValues<ParseOutcome>())
            {
                json.WriteNumber(Camel(outcome.ToString()), _outcomes.GetValueOrDefault(outcome));
            }

            json.WriteEndObject();

            json.WriteStartObject("types");
            foreach (var (type, row) in _types)
            {
                json.WriteStartObject(Camel(type.ToString()));
                json.WriteNumber("parsed", row[0]);
                json.WriteNumber("unsupported", row[1]);
                json.WriteNumber("malformed", row[2]);
                json.WriteEndObject();
            }

            json.WriteEndObject();

            json.WriteStartArray("issues");
            foreach (var ((code, severity), group) in Ordered(_issues))
            {
                json.WriteStartObject();
                json.WriteString("code", code.ToString());
                json.WriteString("severity", Severity(severity));
                json.WriteString("description", code.Describe());
                json.WriteNumber("packets", group.Count);
                WriteExamples(json, group);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("undecoded");
            foreach (var (identifier, group) in Ordered(_undecoded))
            {
                json.WriteStartObject();
                json.WriteString("identifier", identifier);
                json.WriteNumber("packets", group.Count);
                WriteExamples(json, group);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The data type identifier as printed: <c>'{'</c>, or <c>0x1C</c> for an unprintable byte.</summary>
    private static string Identifier(AprsPacket packet)
    {
        var information = packet.Information.Span;
        if (information.IsEmpty)
        {
            return "(empty)";
        }

        var b = information[0];
        return b is >= 0x21 and <= 0x7E ? $"'{(char)b}'" : string.Create(CultureInfo.InvariantCulture, $"0x{b:X2}");
    }

    private static IEnumerable<KeyValuePair<TKey, Group>> Ordered<TKey>(Dictionary<TKey, Group> groups)
        where TKey : notnull => groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key.ToString());

    private static void AppendExamples(StringBuilder text, Group group)
    {
        foreach (var (line, location) in group.Examples)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"      {location}  {LineFormatter.EscapeForConsole(line)}");
        }
    }

    private static void WriteExamples(Utf8JsonWriter json, Group group)
    {
        json.WriteStartArray("examples");
        foreach (var (line, location) in group.Examples)
        {
            json.WriteStartObject();
            json.WriteString("location", location);
            json.WriteString("line", line);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private string Percent(int count) =>
        Packets == 0 ? "" : (100.0 * count / Packets).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Count(int value, string noun) => $"{N(value)} {noun}{(value == 1 ? "" : "s")}";

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Severity(ParseIssueSeverity severity) => severity.ToString().ToLowerInvariant();

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private sealed class Group
    {
        public int Count { get; private set; }

        public List<(string Line, string Location)> Examples { get; } = [];

        public void Add(string line, string location, int limit)
        {
            Count++;
            if (Examples.Count < limit)
            {
                Examples.Add((line, location));
            }
        }
    }
}
