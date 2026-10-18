namespace AprsNet.Kiss;

/// <summary>
/// How to switch a TNC into KISS mode and back out: for a radio's built-in TNC or a TNC with its own command line,
/// which talks text until told to speak KISS. A TNC that only speaks KISS, such as the TNC-X, the NinoTNC or Dire Wolf,
/// needs none.
/// </summary>
/// <remarks>
/// Set <c>KissSerialOptions.Setup</c> and the connection runs <see cref="EnterKiss"/> each time it opens the port,
/// including after a reconnect, and opens the port again when the TNC starts sending text (a power cut can leave KISS
/// off); it runs <see cref="LeaveKiss"/> when it closes. Neither script transmits anything on the air: they only talk
/// to the TNC. <see cref="Known"/> lists the built-in setups.
/// </remarks>
/// <param name="Id">A short name for settings and the command line, such as <c>th-d72</c>.</param>
/// <param name="Name">The name for people, such as <c>Kenwood TH-D72</c>.</param>
/// <param name="EnterKiss">Switches the TNC into KISS mode from wherever it is.</param>
/// <param name="LeaveKiss">Switches it back to its command line, or <see langword="null"/> to leave it in KISS.</param>
public sealed record TncSetup(string Id, string Name, TncScript EnterKiss, TncScript? LeaveKiss)
{
    /// <summary>The KISS frame that tells a TNC to leave KISS mode: <c>C0 FF C0</c>.</summary>
    public static ReadOnlyMemory<byte> KissExitFrame { get; } = new byte[] { 0xC0, 0xFF, 0xC0 };

    /// <summary>What to tell someone when <see cref="EnterKiss"/> fails, such as which mode the radio must be in.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// The Kenwood TH-D72A and TH-D72E. Their TNC must be in packet mode (PACKET12, set on the radio): in APRS mode it
    /// can't be switched. KISS ON doesn't survive a power cut. Leaves KISS with the exit frame, after which the TNC
    /// restarts at its <c>cmd:</c> prompt. Recorded on a TH-D72A with TNC firmware "Version 2.0, Release 16/Nov/2010".
    /// </summary>
    public static TncSetup KenwoodThD72 { get; } =
        new(
            "th-d72",
            "Kenwood TH-D72",
            TncScript.Parse(
                """
                # Leave KISS if it was on: at the cmd: prompt the frame is a bad command, answered with ?EH.
                bytes C0 FF C0
                pause 500ms
                send
                expect 3s cmd:
                # It answers "KISS     was OFF" (or ON), then prompts again.
                send KISS ON
                expect 3s was
                expect 3s cmd:
                # It restarts into KISS without a word.
                send RESTART
                pause 1s
                """
            ),
            new TncScript([TncScriptStep.Send(KissExitFrame.Span)])
        )
        {
            Hint = "Switch the radio's TNC to packet mode (PACKET12) first: in APRS mode it can't change to KISS.",
        };

    /// <summary>The built-in setups, for a settings picker.</summary>
    public static IReadOnlyList<TncSetup> Known { get; } = [KenwoodThD72];

    /// <summary>The built-in setup with <paramref name="id"/>, without regard to case, or <see langword="null"/>.</summary>
    public static TncSetup? Find(string id) =>
        Known.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}
