#:property TargetFramework=net10.0
#:property PublishAot=false
// Writes the release's software bill of materials: one CycloneDX JSON file for every package `dotnet pack` made,
// aprscli's included, and everything they depend on.
//
//   dotnet pack -c Release -o artifacts
//   dotnet run tools/ci/Sbom.cs -- --packages artifacts
//
// The CycloneDX tool (pinned in .config/dotnet-tools.json) lists the NuGet packages the packed projects use, with
// their hashes, licences and the graph between them, leaving out build-only ones such as analyzers and MinVer. It
// knows nothing of the projects themselves, so this adds each package from the .nupkg (its version, SHA-512 and
// licence) and what each one references directly, from the project's restore output. Every reference must be in the
// BOM, so a gap fails here rather than reaching a release.
//
// Writes aprsnet-<version>.cdx.json next to the packages, or to --out.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string? packagesPath = null;
string? outPath = null;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--packages":
            packagesPath = args[++i];
            break;
        case "--out":
            outPath = args[++i];
            break;
    }
}

if (packagesPath is null)
{
    Console.Error.WriteLine("Usage: dotnet run tools/ci/Sbom.cs -- --packages <dir> [--out <file>]");
    return 2;
}

var root = RepositoryRoot();
var packages = Directory
    .GetFiles(packagesPath, "*.nupkg")
    .Select(ReadPackage)
    .OrderBy(p => p.Id, StringComparer.Ordinal)
    .ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine($"No .nupkg files in {packagesPath}.");
    return 1;
}

var versions = packages.Select(p => p.Version).Distinct().ToList();
if (versions.Count != 1)
{
    Console.Error.WriteLine($"The packages have more than one version: {string.Join(", ", versions)}.");
    return 1;
}

var version = versions[0];
outPath ??= Path.Combine(packagesPath, $"aprsnet-{version}.cdx.json");

// CycloneDX reads a solution filter, so it gets one naming the packed projects.
var projects = packages.ToDictionary(p => p.Id, p => FindProject(p.Id));
var work = Directory.CreateTempSubdirectory("aprsnet-sbom-");
var filter = Path.Combine(root, $"aprsnet-sbom-{Environment.ProcessId}.slnf");
try
{
    File.WriteAllText(
        filter,
        new JsonObject
        {
            ["solution"] = new JsonObject
            {
                ["path"] = "AprsNet.slnx",
                ["projects"] = new JsonArray([
                    .. projects.Values.Select(p => (JsonNode)Path.GetRelativePath(root, p).Replace('\\', '/')),
                ]),
            },
        }.ToJsonString()
    );
    Run(
        "dotnet",
        [
            "CycloneDX",
            filter,
            "--output",
            work.FullName,
            "--filename",
            "bom.json",
            "--output-format",
            "Json",
            "--exclude-dev",
            "--set-name",
            "APRS.NET",
            "--set-version",
            version,
            "--set-type",
            "Library",
        ]
    );
}
finally
{
    File.Delete(filter);
}

var bom = JsonNode.Parse(File.ReadAllText(Path.Combine(work.FullName, "bom.json")))!.AsObject();
work.Delete(recursive: true);

var components = bom["components"]!.AsArray();
var dependencies = bom["dependencies"]!.AsArray();
var known = components.Select(c => (string)c!["bom-ref"]!).ToHashSet(StringComparer.Ordinal);
var rootRef = (string)bom["metadata"]!["component"]!["bom-ref"]!;

foreach (var package in packages)
{
    components.Add(package.Component());
    known.Add(package.Ref);
}

// CycloneDX makes the root depend on every package any project references; the release is made of its own packages.
for (var i = dependencies.Count - 1; i >= 0; i--)
{
    if ((string)dependencies[i]!["ref"]! == rootRef)
    {
        dependencies.RemoveAt(i);
    }
}

dependencies.Add(new JsonObject { ["ref"] = rootRef, ["dependsOn"] = Refs(packages.Select(p => p.Ref)) });
var missing = new List<string>();
foreach (var package in packages)
{
    var references = DirectReferences(projects[package.Id], packages, version);
    missing.AddRange(references.Where(r => !known.Contains(r)).Select(r => $"{package.Id} -> {r}"));
    dependencies.Add(new JsonObject { ["ref"] = package.Ref, ["dependsOn"] = Refs(references) });
}

if (missing.Count > 0)
{
    Console.Error.WriteLine("References missing from the BOM:");
    missing.ForEach(m => Console.Error.WriteLine($"  {m}"));
    return 1;
}

File.WriteAllText(outPath, bom.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(
    $"Wrote {outPath}: {packages.Count} packages of ours and {components.Count - packages.Count} they depend on."
);
return 0;

static JsonArray Refs(IEnumerable<string> refs) => [.. refs.Order(StringComparer.Ordinal).Select(r => (JsonNode)r)];

static string Purl(string id, string version) => $"pkg:nuget/{id}@{version}";

// A project's direct references, as packages: its project references at the release's version, and the packages it
// references at the versions restore chose, leaving out build-only ones (PrivateAssets="all").
static List<string> DirectReferences(string project, List<Package> packages, string version)
{
    var assets = JsonNode.Parse(
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"))
    )!;
    var references = new List<string>();
    foreach (var (framework, node) in assets["project"]!["frameworks"]!.AsObject())
    {
        var resolved = assets["targets"]![framework]!.AsObject();
        foreach (var (id, dependency) in node!["dependencies"]?.AsObject() ?? [])
        {
            if ((string?)dependency!["suppressParent"] == "All")
            {
                continue;
            }

            var key = resolved
                .Select(t => t.Key)
                .Single(k => k.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase));
            var (name, resolvedVersion) = (key.Split('/')[0], key.Split('/')[1]);
            references.Add(Purl(name, resolvedVersion));
        }

        foreach (
            var (path, _) in assets["project"]!["restore"]!["frameworks"]![framework]!["projectReferences"]?.AsObject()
                ?? []
        )
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var package =
                packages.SingleOrDefault(p => p.Id == name)
                ?? throw new InvalidOperationException($"{project} references {name}, which wasn't packed.");
            references.Add(Purl(package.Id, version));
        }
    }

    return [.. references.Distinct(StringComparer.Ordinal)];
}

static Package ReadPackage(string path)
{
    using var zip = ZipFile.OpenRead(path);
    var entry = zip.Entries.Single(e =>
        !e.FullName.Contains('/', StringComparison.Ordinal) && e.Name.EndsWith(".nuspec", StringComparison.Ordinal)
    );
    using var stream = entry.Open();
    var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    string? Get(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;
    var repository = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "repository");
    var isTool = metadata
        .Elements()
        .Where(e => e.Name.LocalName == "packageTypes")
        .Elements()
        .Any(e => (string?)e.Attribute("name") == "DotnetTool");

    return new Package(
        Get("id")!,
        Get("version")!,
        Get("description"),
        Get("authors"),
        Get("copyright"),
        License(zip, metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "license")),
        (string?)repository?.Attribute("url"),
        (string?)repository?.Attribute("commit"),
        isTool,
        Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path)))
    );
}

// A licence expression of one SPDX ID is given as that ID, as CycloneDX gives other packages'; a licence file (as
// AprsNet.DeviceId's, which covers its code and its device table separately) is named and embedded.
static JsonObject? License(ZipArchive zip, XElement? license)
{
    if (license is null)
    {
        return null;
    }

    if ((string?)license.Attribute("type") == "expression")
    {
        return license.Value.Contains(' ', StringComparison.Ordinal)
            ? new JsonObject { ["expression"] = license.Value }
            : new JsonObject { ["license"] = new JsonObject { ["id"] = license.Value } };
    }

    using var file = zip.GetEntry(license.Value)!.Open();
    using var text = new MemoryStream();
    file.CopyTo(text);
    return new JsonObject
    {
        ["license"] = new JsonObject
        {
            ["name"] = $"{license.Value} in the package",
            ["text"] = new JsonObject
            {
                ["contentType"] = "text/markdown",
                ["encoding"] = "base64",
                ["content"] = Convert.ToBase64String(text.ToArray()),
            },
        },
    };
}

string FindProject(string id)
{
    var found = Directory
        .GetFiles(Path.Combine(root, "src"), $"{id}.csproj", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(Path.Combine(root, "apps"), $"{id}.csproj", SearchOption.AllDirectories))
        .ToList();
    return found.Count == 1
        ? found[0]
        : throw new InvalidOperationException($"Expected one project for the package {id}, found {found.Count}.");
}

static void Run(string file, string[] arguments)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine(output);
        throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} exited with {process.ExitCode}.");
    }
}

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AprsNet.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Run this from inside the repository.");
}

sealed record Package(
    string Id,
    string Version,
    string? Description,
    string? Authors,
    string? Copyright,
    JsonObject? License,
    string? Repository,
    string? Commit,
    bool IsTool,
    string Sha512
)
{
    public string Ref => $"pkg:nuget/{Id}@{Version}";

    public JsonObject Component()
    {
        var component = new JsonObject
        {
            ["type"] = IsTool ? "application" : "library",
            ["bom-ref"] = Ref,
            ["authors"] = new JsonArray(new JsonObject { ["name"] = Authors }),
            ["name"] = Id,
            ["version"] = Version,
            ["description"] = Description,
            ["scope"] = "required",
            ["hashes"] = new JsonArray(new JsonObject { ["alg"] = "SHA-512", ["content"] = Sha512 }),
            ["licenses"] = License is null ? null : new JsonArray(License),
            ["copyright"] = Copyright,
            ["purl"] = Ref,
        };
        if (Repository is not null)
        {
            component["externalReferences"] = new JsonArray(
                new JsonObject
                {
                    ["url"] = Repository,
                    ["type"] = "vcs",
                    ["comment"] = Commit is null ? null : $"commit {Commit}",
                }
            );
        }

        return component;
    }
}
