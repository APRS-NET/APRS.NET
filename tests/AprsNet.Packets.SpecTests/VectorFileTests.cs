using System.Text.Json;
using System.Text.RegularExpressions;

namespace AprsNet.Packets.SpecTests;

/// <summary>
/// Checks the APRS101 test vectors themselves until the parser exists to run them: well-formed files, unique ids, known
/// features, and every vector matching the feature patterns it claims (with the .NET regex engine the coverage tool uses).
/// </summary>
public class VectorFileTests
{
    private static readonly string _directory = Path.Combine(AppContext.BaseDirectory, "Vectors");

    private static readonly HashSet<string?> _statuses = ["normative", "derived", "erratum-corrected", "superseded"];

    private static readonly HashSet<string> _targets = ["info", "line", "dest"];

    public static TheoryData<string> VectorIds()
    {
        var data = new TheoryData<string>();
        foreach (var vector in _vectors.Value)
        {
            data.Add(vector.GetProperty("id").GetString()!);
        }

        return data;
    }

    private static readonly Lazy<List<JsonElement>> _vectors = new(() =>
        Directory
            .GetFiles(_directory, "aprs101-*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path =>
                JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("vectors").EnumerateArray()
            )
            .ToList()
    );

    private static readonly Lazy<Dictionary<string, (string Target, Regex Match)>> _features = new(() =>
        JsonDocument
            .Parse(File.ReadAllText(Path.Combine(_directory, "features.json")))
            .RootElement.GetProperty("features")
            .EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("id").GetString()!,
                f => (f.GetProperty("target").GetString()!, new Regex(f.GetProperty("match").GetString()!))
            )
    );

    [Fact]
    public void ThereAreVectorsForEveryChapterFromFourToTwenty()
    {
        var chapters = _vectors.Value.Select(v => v.GetProperty("id").GetString()![..4]).ToHashSet();

        Assert.Equal(16, chapters.Count); // chapter 5 is covered by the position and other-packet chapters
        Assert.True(_vectors.Value.Count >= 100, $"only {_vectors.Value.Count} vectors");
    }

    [Fact]
    public void IdsAreUnique()
    {
        var duplicates = _vectors
            .Value.GroupBy(v => v.GetProperty("id").GetString())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void FeaturePatternsAreValidForEveryTarget()
    {
        Assert.All(_features.Value, f => Assert.Contains(f.Value.Target, _targets));
    }

    [Theory]
    [MemberData(nameof(VectorIds))]
    public void VectorIsWellFormedAndMatchesItsFeatures(string id)
    {
        var vector = _vectors.Value.Single(v => v.GetProperty("id").GetString() == id);

        Assert.Contains(vector.GetProperty("status").GetString(), _statuses);
        Assert.Equal("APRS101", vector.GetProperty("spec").GetProperty("document").GetString());
        Assert.InRange(vector.GetProperty("spec").GetProperty("page").GetInt32(), 1, 128);
        Assert.Equal(JsonValueKind.Object, vector.GetProperty("expect").ValueKind);

        var packet = vector.GetProperty("packet").GetString()!;
        var (info, destination) = Split(packet);
        foreach (var feature in vector.GetProperty("features").EnumerateArray().Select(f => f.GetString()!))
        {
            Assert.True(_features.Value.TryGetValue(feature, out var definition), $"unknown feature {feature}");
            var target = definition.Target switch
            {
                "info" => info,
                "dest" => destination,
                _ => packet,
            };
            Assert.True(definition.Match.IsMatch(target), $"{id}: feature {feature} does not match {packet}");
        }
    }

    /// <summary>Information field (after the first ':') and destination callsign without SSID.</summary>
    private static (string Info, string Destination) Split(string packet)
    {
        var colon = packet.IndexOf(':', StringComparison.Ordinal);
        Assert.True(colon > 0, $"no ':' in {packet}");
        var header = packet[..colon];
        var gt = header.IndexOf('>', StringComparison.Ordinal);
        Assert.True(gt > 0, $"no '>' in {packet}");
        var destination = header[(gt + 1)..].Split(',')[0].Split('-')[0];
        return (packet[(colon + 1)..], destination);
    }
}
