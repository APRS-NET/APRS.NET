namespace AprsNet.Corpus.Tests;

public sealed class CorpusAnonymizerTests
{
    private static readonly DateTimeOffset _at = new(2026, 10, 12, 3, 21, 18, TimeSpan.Zero);

    private static CorpusLine Line(string packet, int second = 0) => new(_at.AddSeconds(second), packet);

    [Fact]
    public void CallsignsNamesAndEmailAreReplacedAndTheRestIsKept()
    {
        var result = new CorpusAnonymizer().Anonymize([
            Line("K1ABC-9>APRS,WIDE2-1,qAR,W2XYZ-10:!4903.50N/07201.75W-On the air k1abc@example.com"),
            Line("KCSTORM>APDW16,TCPIP*,qAC,T2TEST:>Weather by W2XYZ"),
        ]);

        Assert.True(result.IsClean);
        Assert.Equal(
            [
                "Q1AAA-9>APRS,WIDE2-1,qAR,Q2AAA-10:!4903.50N/07201.75W-On the air x0xxx@xxxxxxx.xxx",
                "XAAAAAA>APDW16,TCPIP*,qAC,T2TEST:>Weather by Q2AAA",
            ],
            result.Lines.Select(l => l.Packet)
        );
        Assert.Equal([_at, _at], result.Lines.Select(l => l.ReceivedAt));
    }

    [Fact]
    public void EveryPacketKeepsItsLength()
    {
        string[] packets =
        [
            "JA1ABC-7>S9SSQT,WIDE1-1,qAR,7K1XYZ:`pB[m!Tk/]\"HV}145.190MHz JA1ABC=",
            "K1ABC>APRS::W2XYZ-7  :Hello there{12",
            "K1ABC>APRS:;K1ABC-R  *111111z4903.50N/07201.75Wr145.230MHz",
        ];

        var result = new CorpusAnonymizer().Anonymize(packets.Select(p => Line(p)));

        Assert.Equal(packets.Select(p => p.Length), result.Lines.Select(l => l.Packet.Length));
        Assert.True(result.IsClean);
    }

    [Fact]
    public void TheTocallMicEDestinationAndGenericAliasesAreKept()
    {
        var result = new CorpusAnonymizer().Anonymize([Line("JA1ABC-7>S9SSQT,WIDE1-1,qAR,T2TOKYO:`pB[m!Tk/]\"HV}")]);

        Assert.StartsWith(
            "QA1AAA-7>S9SSQT,WIDE1-1,qAR,T2TOKYO:`pB[m!Tk/]",
            result.Lines[0].Packet,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AStationMentionedBeforeItIsHeardIsStillReplaced()
    {
        var result = new CorpusAnonymizer().Anonymize([
            Line("K1ABC>APRS:>Listening for W2XYZ"),
            Line("W2XYZ-9>APRS:>Here", 1),
        ]);

        Assert.DoesNotContain("W2XYZ", result.Lines[0].Packet, StringComparison.Ordinal);
        Assert.True(result.IsClean);
    }

    [Fact]
    public void PseudonymsCarryOverToTheNextExport()
    {
        var first = new CorpusAnonymizer();
        var earlier = first.Anonymize([Line("K1ABC-9>APRS:>first"), Line("W2XYZ>APRS:>first")]);

        var saved = CorpusPseudonyms.FromJson(first.Pseudonyms.ToJson());
        var later = new CorpusAnonymizer(saved).Anonymize([Line("W2XYZ-5>APRS:>later")]);

        Assert.Equal(earlier.Lines[1].Packet[..5], later.Lines[0].Packet[..5]);
        Assert.Equal(first.Pseudonyms.Calls, saved.Calls);
    }

    [Fact]
    public void ThePseudonymFileKeepsTheScriptsShape()
    {
        var anonymizer = new CorpusAnonymizer();
        anonymizer.Anonymize([Line("K1ABC>APRS:>x")]);
        anonymizer.Pseudonyms.Literals["Alice"] = "Xxxxx";

        Assert.Equal(
            """
            {
              "Calls": {
                "K1ABC": "Q1AAA"
              },
              "Names": {},
              "Literals": {
                "Alice": "Xxxxx"
              }
            }
            """.ReplaceLineEndings("\n"),
            anonymizer.Pseudonyms.ToJson().ReplaceLineEndings("\n")
        );
    }

    [Fact]
    public void LiteralsReplaceTextByHand()
    {
        var pseudonyms = new CorpusPseudonyms { Literals = { ["Alice"] = "Xxxxx" } };

        var result = new CorpusAnonymizer(pseudonyms).Anonymize([Line("K1ABC>APRS:>Alice's tracker")]);

        Assert.Equal("Q1AAA>APRS:>Xxxxx's tracker", result.Lines[0].Packet);
    }

    [Fact]
    public void LinesCarryingKissFramesAreLeftOut()
    {
        var kiss = Line("K1ABC>APRS,qAR,W2XYZ:}À\u0000\u0082 ¤¦@@`");

        var result = new CorpusAnonymizer().Anonymize([Line("K1ABC>APRS:>fine"), kiss]);

        Assert.Equal(kiss, Assert.Single(result.LeftOut));
        Assert.Single(result.Lines);
        Assert.True(CorpusAnonymizer.HasKissFrame("xÃ\u0080y")); // a frame re-encoded as UTF-8 on the way
    }

    [Fact]
    public void LeaksNameTheIdentifierStillPresent()
    {
        var anonymizer = new CorpusAnonymizer();
        anonymizer.Learn("K1ABC>APRS:>x");

        var leaks = anonymizer.FindLeaks(["Q1AAA>APRS:>x", "Q1AAA>APRS:>73 de k1abc"]);

        Assert.Equal(["K1ABC: Q1AAA>APRS:>73 de k1abc"], leaks);
    }

    [Fact]
    public void LearningAndRewritingCanBeDoneInSteps()
    {
        var anonymizer = new CorpusAnonymizer();
        anonymizer.Learn("K1ABC>APRS:>x");

        Assert.Equal("Q1AAA>APRS:>y", anonymizer.Rewrite("K1ABC>APRS:>y"));
        Assert.Equal("N0NE>APRS:>unlearned", anonymizer.Rewrite("N0NE>APRS:>unlearned"));
    }
}
