using System.Text.RegularExpressions;

namespace AprsNet.Docs.Tests;

/// <summary>
/// Keeps the guides' code compiling: every C# block in docs/guides must follow a <c>&lt;!-- snippet: name --&gt;</c>
/// comment and equal the <c>#region name</c> in Snippets/, which this project builds.
/// </summary>
public sealed partial class DocsSnippetTests
{
    private static readonly string Root = FindRoot();

    public static TheoryData<string> Guides() =>
        [.. Directory.GetFiles(Path.Combine(Root, "docs", "guides"), "*.md").Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(Guides))]
    public void EveryCodeBlockIsACompiledSnippet(string guide)
    {
        var text = File.ReadAllText(Path.Combine(Root, "docs", "guides", guide)).ReplaceLineEndings("\n");
        var regions = Regions();
        var blocks = Snippet().Matches(text);

        // A guide may have no C# at all, such as the container guide; any it has must be compiled snippets.
        Assert.Equal(CSharpBlock().Count(text), blocks.Count);
        foreach (Match block in blocks)
        {
            var name = block.Groups["name"].Value;
            Assert.True(regions.TryGetValue(name, out var code), $"No #region {name} in Snippets/.");
            Assert.Equal(code, block.Groups["code"].Value.TrimEnd('\n'));
        }
    }

    [Fact]
    public void EverySnippetIsInAGuide()
    {
        var guides = string.Concat(
            Directory.GetFiles(Path.Combine(Root, "docs", "guides"), "*.md").Select(File.ReadAllText)
        );

        Assert.All(
            Regions().Keys,
            name => Assert.Contains($"<!-- snippet: {name} -->", guides, StringComparison.Ordinal)
        );
    }

    /// <summary>Each <c>#region</c> in Snippets/, dedented, by name.</summary>
    private static Dictionary<string, string> Regions()
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "tests", "AprsNet.Docs.Tests", "Snippets"), "*.cs"))
        {
            foreach (Match region in Region().Matches(File.ReadAllText(file).ReplaceLineEndings("\n")))
            {
                var lines = region.Groups["code"].Value.TrimEnd('\n').Split('\n');
                var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
                regions.Add(
                    region.Groups["name"].Value,
                    string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()))
                );
            }
        }

        return regions;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("*.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(
        @"^[ \t]*#region (?<name>\S+)\n(?<code>.*?)^[ \t]*#endregion",
        RegexOptions.Multiline | RegexOptions.Singleline
    )]
    private static partial Regex Region();

    [GeneratedRegex(@"<!-- snippet: (?<name>\S+) -->\n+```csharp\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex Snippet();

    [GeneratedRegex(@"```csharp\n")]
    private static partial Regex CSharpBlock();
}
