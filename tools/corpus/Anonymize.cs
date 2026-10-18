#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../../src/AprsNet.Corpus/AprsNet.Corpus.csproj

// Anonymizes aprscli capture files ("<UTC timestamp> <raw line>", Latin-1) for tests/corpus.
//
//   dotnet run tools/corpus/Anonymize.cs -- --map <mapping.json> --out <file> [--drop-binary] <capture>...
//
// The rules live in AprsNet.Corpus (CorpusAnonymizer), so apps that export captures apply the same ones. Callsigns
// become same-length Q-prefixed calls, named stations same-length X-names, and e-mail addresses are masked; every line
// keeps its length. Positions, device TOCALLs, Mic-E destinations, generic path aliases and APRS-IS server names are kept
// byte for byte.
//
// The mapping file is the key to undo the anonymization: keep it OUT of the repository. It is read if it exists and
// written back, so later captures reuse the same pseudonyms. Its "Literals" section can hold extra same-length
// replacements (for example a person's name found in a comment) that should never be committed either.

using AprsNet.Corpus;

string? mapPath = null;
string? outPath = null;
var dropBinary = false;
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--map":
            mapPath = args[++i];
            break;
        case "--out":
            outPath = args[++i];
            break;
        case "--drop-binary":
            dropBinary = true;
            break;
        default:
            inputs.Add(args[i]);
            break;
    }
}

if (mapPath is null || outPath is null || inputs.Count == 0)
{
    Console.Error.WriteLine("usage: Anonymize.cs --map <mapping.json> --out <file> [--drop-binary] <capture>...");
    return 3;
}

var pseudonyms = File.Exists(mapPath) ? CorpusPseudonyms.FromJson(File.ReadAllText(mapPath)) : new CorpusPseudonyms();
var result = new CorpusAnonymizer(pseudonyms).Anonymize(inputs.SelectMany(CorpusFile.ReadLines).ToList());

// A broken gateway can leak KISS frames into a packet: binary AX.25 headers hold callsigns as shifted bytes, which
// the text rules cannot see or rewrite. Refuse them, or drop them with --drop-binary.
if (result.LeftOut.Count > 0)
{
    if (!dropBinary)
    {
        Console.Error.WriteLine(
            $"{result.LeftOut.Count} lines carry KISS frames whose binary AX.25 headers cannot be anonymized; "
                + "rerun with --drop-binary to leave them out:"
        );
        foreach (var line in result.LeftOut)
        {
            Console.Error.WriteLine($"  {line}");
        }

        return 1;
    }

    Console.WriteLine($"dropped {result.LeftOut.Count} lines carrying KISS frames");
}

if (!result.IsClean)
{
    Console.Error.WriteLine("original identifiers still present:");
    foreach (var leak in result.Leaks)
    {
        Console.Error.WriteLine($"  {leak}");
    }

    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
CorpusFile.WriteLines(outPath, result.Lines);
File.WriteAllText(mapPath, pseudonyms.ToJson());
Console.WriteLine(
    $"{result.Lines.Count} lines -> {outPath}; {pseudonyms.Calls.Count} callsigns, {pseudonyms.Names.Count} station names mapped"
);
return 0;
