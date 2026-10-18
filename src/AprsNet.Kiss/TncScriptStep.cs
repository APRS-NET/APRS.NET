using System.Globalization;
using System.Text;

namespace AprsNet.Kiss;

/// <summary>What a <see cref="TncScriptStep"/> does.</summary>
public enum TncScriptStepKind
{
    /// <summary>Send bytes to the TNC: a command and its carriage return, or raw bytes such as a KISS frame.</summary>
    Send,

    /// <summary>Wait until the TNC sends some text, such as its <c>cmd:</c> prompt, failing after a timeout.</summary>
    Expect,

    /// <summary>Wait a set time, for a TNC that needs a moment between commands.</summary>
    Pause,
}

/// <summary>
/// One step of a <see cref="TncScript"/>: send something to the TNC, expect something back, or pause. Create steps
/// with <see cref="SendLine"/>, <see cref="Send(ReadOnlySpan{byte})"/>, <see cref="Expect"/> and <see cref="Pause"/>.
/// </summary>
public sealed class TncScriptStep
{
    private TncScriptStep(TncScriptStepKind kind, byte[] bytes, string text, TimeSpan time)
    {
        Kind = kind;
        Bytes = bytes;
        Text = text;
        Time = time;
    }

    /// <summary>What the step does.</summary>
    public TncScriptStepKind Kind { get; }

    /// <summary>For <see cref="TncScriptStepKind.Send"/>, the bytes sent.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>For <see cref="TncScriptStepKind.Expect"/>, the text waited for; matched without regard to case.</summary>
    public string Text { get; }

    /// <summary>
    /// For <see cref="TncScriptStepKind.Expect"/>, how long to wait before failing; for
    /// <see cref="TncScriptStepKind.Pause"/>, how long to pause.
    /// </summary>
    public TimeSpan Time { get; }

    /// <summary>Sends <paramref name="command"/> and a carriage return, as typed at the TNC's prompt.</summary>
    /// <exception cref="ArgumentException">The command holds a character above U+00FF.</exception>
    public static TncScriptStep SendLine(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.AsSpan().IndexOfAnyExceptInRange('\0', 'ÿ') >= 0)
        {
            throw new ArgumentException("A TNC command must be Latin-1 text, one byte per character.", nameof(command));
        }

        return new(TncScriptStepKind.Send, Encoding.Latin1.GetBytes(command + "\r"), command, TimeSpan.Zero);
    }

    /// <summary>Sends <paramref name="bytes"/> as they are, such as the KISS exit frame <c>C0 FF C0</c>.</summary>
    public static TncScriptStep Send(ReadOnlySpan<byte> bytes) =>
        new(TncScriptStepKind.Send, bytes.ToArray(), "", TimeSpan.Zero);

    /// <summary>
    /// Waits until the TNC sends <paramref name="text"/>, matched without regard to case, failing after
    /// <paramref name="timeout"/>. Only what arrived since the previous expected text was matched counts.
    /// </summary>
    /// <exception cref="ArgumentException">The text is empty, or the timeout is not positive.</exception>
    public static TncScriptStep Expect(string text, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        return new(TncScriptStepKind.Expect, [], text, timeout);
    }

    /// <summary>Pauses for <paramref name="time"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The time is negative.</exception>
    public static TncScriptStep Pause(TimeSpan time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(time, TimeSpan.Zero);
        return new(TncScriptStepKind.Pause, [], "", time);
    }

    /// <summary>The step as one line of a script's text form; see <see cref="TncScript.Parse"/>.</summary>
    public override string ToString() =>
        Kind switch
        {
            TncScriptStepKind.Send when Text.Length > 0 || Bytes.Length == 1 && Bytes.Span[0] == '\r' => $"send {Text}",
            TncScriptStepKind.Send => $"bytes {Convert.ToHexString(Bytes.Span)}",
            TncScriptStepKind.Expect =>
                $"expect {Time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s {Text}",
            _ => $"pause {Time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s",
        };
}
