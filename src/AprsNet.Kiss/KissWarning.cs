namespace AprsNet.Kiss;

/// <summary>Something wrong with what a TNC is sending, while the connection itself is up.</summary>
public enum KissWarning
{
    /// <summary>
    /// The TNC is sending text lines, not KISS frames: it isn't in KISS mode (a radio's built-in TNC in its APRS mode,
    /// or one that came back from a power cut with KISS off). Nothing it sends can be read until it is.
    /// </summary>
    TextNotKiss,
}

/// <summary>A <see cref="KissWarning"/> starting or ending.</summary>
public sealed class KissWarningEventArgs : EventArgs
{
    internal KissWarningEventArgs(KissWarning warning, bool isActive)
    {
        Warning = warning;
        IsActive = isActive;
    }

    /// <summary>The warning.</summary>
    public KissWarning Warning { get; }

    /// <summary>
    /// <see langword="true"/> when it starts; <see langword="false"/> when it is over, for example once KISS frames
    /// arrive again.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>A sentence for a log or a status line, saying what is wrong or that it is over.</summary>
    public string Message =>
        (Warning, IsActive) switch
        {
            (KissWarning.TextNotKiss, true) => "The TNC is sending text, not KISS frames; is it in KISS mode?",
            (KissWarning.TextNotKiss, false) => "The TNC is sending KISS frames again.",
            _ => Warning.ToString(),
        };
}
