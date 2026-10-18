namespace AprsNet.Corpus;

/// <summary>What <see cref="CorpusAnonymizer.Anonymize"/> made of a set of lines.</summary>
public sealed class CorpusAnonymization
{
    internal CorpusAnonymization(
        IReadOnlyList<CorpusLine> lines,
        IReadOnlyList<CorpusLine> leftOut,
        IReadOnlyList<string> leaks
    )
    {
        Lines = lines;
        LeftOut = leftOut;
        Leaks = leaks;
    }

    /// <summary>The anonymized lines, in their original order, each exactly as long as before.</summary>
    public IReadOnlyList<CorpusLine> Lines { get; }

    /// <summary>
    /// Lines left out because they carry KISS frames (see <see cref="CorpusAnonymizer.HasKissFrame"/>): the callsigns
    /// in a binary AX.25 header can't be rewritten. They are the originals, not anonymized.
    /// </summary>
    public IReadOnlyList<CorpusLine> LeftOut { get; }

    /// <summary>
    /// Original identifiers still found in <see cref="Lines"/>, each as <c>identifier: packet</c>. Don't share the
    /// lines unless this is empty. The entries name the original identifiers, so treat them as private too.
    /// </summary>
    public IReadOnlyList<string> Leaks { get; }

    /// <summary>Whether no original identifier was found in <see cref="Lines"/>.</summary>
    public bool IsClean => Leaks.Count == 0;
}
