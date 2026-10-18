using System.Text.Json;
using System.Text.Json.Serialization;

namespace AprsNet.Corpus;

/// <summary>
/// The pseudonyms a <see cref="CorpusAnonymizer"/> has handed out, so later exports reuse them: the same station gets
/// the same pseudonym every time. This is the key to undo the anonymization, so keep it private and never publish it
/// beside the corpus.
/// </summary>
/// <remarks>
/// <see cref="ToJson"/> and <see cref="FromJson"/> read and write the same file as <c>tools/corpus/Anonymize.cs</c>
/// (its <c>--map</c>).
/// </remarks>
public sealed class CorpusPseudonyms
{
    /// <summary>Base callsigns, upper case and without SSID, and their pseudonyms (<c>NZ0P</c> to <c>QA0AA</c>).</summary>
    public SortedDictionary<string, string> Calls { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Station names that aren't callsigns, upper case, and their X-names (<c>KCSTORM</c> to <c>XAAAAAA</c>).</summary>
    public SortedDictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Extra replacements of the same length, made in free text, such as a person's name found in a comment. Edit them
    /// by hand; the anonymizer never adds any.
    /// </summary>
    public SortedDictionary<string, string> Literals { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The pseudonyms as indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, CorpusJsonContext.Default.CorpusPseudonyms);

    /// <summary>Reads pseudonyms written by <see cref="ToJson"/> or by <c>tools/corpus/Anonymize.cs</c>.</summary>
    /// <exception cref="JsonException">The text is not such a file.</exception>
    public static CorpusPseudonyms FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var pseudonyms =
            JsonSerializer.Deserialize(json, CorpusJsonContext.Default.CorpusPseudonyms) ?? new CorpusPseudonyms();

        // JSON gives default comparers; the anonymizer relies on ordinal order.
        pseudonyms.Calls = new SortedDictionary<string, string>(pseudonyms.Calls, StringComparer.Ordinal);
        pseudonyms.Names = new SortedDictionary<string, string>(pseudonyms.Names, StringComparer.Ordinal);
        pseudonyms.Literals = new SortedDictionary<string, string>(pseudonyms.Literals, StringComparer.Ordinal);
        return pseudonyms;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CorpusPseudonyms))]
internal sealed partial class CorpusJsonContext : JsonSerializerContext;
