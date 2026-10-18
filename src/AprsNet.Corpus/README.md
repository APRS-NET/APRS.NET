# AprsNet.Corpus

Packet captures for an APRS test corpus, such as [APRS.NET's](https://github.com/APRS-NET/APRS.NET/tree/main/tests/corpus):
the corpus line format, and an anonymizer that makes captures safe to share.

## The line format

One packet per line: the UTC time it was received, a space, and the raw packet, in Latin-1 so every byte round-trips
(Mic-E data isn't valid UTF-8). `aprscli listen --capture` writes the same format.

```text
2026-10-12T03:21:18.012Z QA0AA-15>APU25N,TCPIP*,qAC,T2MCI:=3853.05N/09448.81WoSATERN EOC
```

```csharp
using AprsNet.Corpus;

await using var capture = CorpusFile.OpenAppend("capture/unparsed.log"); // flushed line by line
await capture.WriteAsync(new CorpusLine(receivedAt, packet.Raw.Span));    // the raw bytes, kept as they are

foreach (var line in CorpusFile.ReadLines("capture/unparsed.log"))
    Console.WriteLine($"{line.ReceivedAt:u} {line.Packet}");
```

## Anonymizing

`CorpusAnonymizer` replaces callsigns with Q-prefixed calls of the same shape (`NZ0P` becomes `QA0AA`; Q starts no ITU
prefix, so none can be real), station names with X-names of the same length, and masks e-mail addresses. Each packet
keeps its length. Positions, TOCALLs, Mic-E destinations, generic path aliases (`WIDE2-1`, `qAC`) and APRS-IS server
names are kept byte for byte.

```csharp
var pseudonyms = File.Exists("pseudonyms.json")
    ? CorpusPseudonyms.FromJson(File.ReadAllText("pseudonyms.json"))
    : new CorpusPseudonyms();

var result = new CorpusAnonymizer(pseudonyms).Anonymize(CorpusFile.ReadLines("capture/unparsed.log"));
if (!result.IsClean)
    throw new InvalidOperationException($"{result.Leaks.Count} original identifiers are still present");

CorpusFile.WriteLines("export.log", result.Lines);
File.WriteAllText("pseudonyms.json", pseudonyms.ToJson()); // the same stations get the same pseudonyms next time
```

The pseudonyms are the key to undo the anonymization: keep them private, never beside the export. Lines carrying KISS
frames, which a broken gateway can leak into a packet, are left out (`result.LeftOut`): the callsigns in their binary
headers can't be rewritten. Free text can still name a person; review it before sharing, and add same-length
replacements to `pseudonyms.Literals`.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).
