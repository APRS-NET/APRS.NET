using System.Globalization;
using System.Text;

namespace AprsNet.Kiss;

/// <summary>
/// Commands for a TNC's own command line, run in order on its port: send a command, expect its prompt back, pause.
/// <see cref="TncSetup"/> uses scripts to switch a TNC into and out of KISS mode.
/// </summary>
/// <remarks>
/// <para>The text form, one step per line, is what <see cref="Parse"/> reads and <see cref="ToString"/> writes:</para>
/// <code>
/// # Leave KISS if it was on, then turn it on at the cmd: prompt.
/// bytes C0 FF C0
/// send
/// expect 3s cmd:
/// send KISS ON
/// pause 500ms
/// send RESTART
/// </code>
/// <para>
/// <c>send</c> sends the rest of the line and a carriage return (just a carriage return when there is nothing after
/// it); <c>bytes</c> sends hex bytes as they are; <c>expect</c> waits up to a time for some text, without regard to
/// case; <c>pause</c> waits; <c>drain</c> waits, then throws away what the TNC sent so far. Times are in <c>ms</c> or
/// <c>s</c>. Blank lines and lines starting with <c>#</c> are ignored.
/// </para>
/// </remarks>
public sealed class TncScript
{
    /// <summary>Creates a script of <paramref name="steps"/>.</summary>
    public TncScript(IEnumerable<TncScriptStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps = [.. steps];
        if (Steps.Any(s => s is null))
        {
            throw new ArgumentException("A script step is null.", nameof(steps));
        }
    }

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<TncScriptStep> Steps { get; }

    /// <summary>Reads a script in its text form; see the remarks on <see cref="TncScript"/>.</summary>
    /// <exception cref="FormatException">A line isn't a step; the message gives its number.</exception>
    public static TncScript Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var steps = new List<TncScriptStep>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var word = (space < 0 ? line : line[..space]).ToLowerInvariant();
            var rest = space < 0 ? "" : line[(space + 1)..].Trim();
            steps.Add(
                word switch
                {
                    "send" => TncScriptStep.SendLine(rest),
                    "bytes" => TncScriptStep.Send(Hex(rest, number)),
                    "expect" => Expect(rest, number),
                    "pause" => TncScriptStep.Pause(Duration(rest, number)),
                    "drain" => TncScriptStep.Drain(Duration(rest, number)),
                    _ => throw new FormatException(
                        $"Line {number}: '{word}' isn't a script step (send, bytes, expect, pause or drain)."
                    ),
                }
            );
        }

        return new TncScript(steps);
    }

    /// <summary>
    /// Everything the script sends, run together, without waiting for anything: what can still be said to a TNC whose
    /// port is closing.
    /// </summary>
    public ReadOnlyMemory<byte> SentBytes() =>
        Steps.Where(s => s.Kind == TncScriptStepKind.Send).SelectMany(s => s.Bytes.ToArray()).ToArray();

    /// <summary>The script in its text form, one step per line.</summary>
    public override string ToString() => string.Join('\n', Steps.Select(s => s.ToString()));

    /// <summary>
    /// Runs the script on <paramref name="stream"/>, the TNC's port. Returns everything the TNC sent while it ran, as
    /// Latin-1 text. Bytes that came after the last expected text may still be unread in the stream.
    /// </summary>
    /// <exception cref="TncScriptException">An expected text didn't come in time, or the TNC closed the port.</exception>
    public Task<string> RunAsync(
        Stream stream,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    ) => RunAsync(stream, timeProvider, trace: null, cancellationToken);

    /// <summary>
    /// <see cref="RunAsync(Stream, TimeProvider?, CancellationToken)"/>, telling <paramref name="trace"/> about each
    /// thing sent, each piece received and a step that failed, for a connection's traffic.
    /// </summary>
    internal async Task<string> RunAsync(
        Stream stream,
        TimeProvider? timeProvider,
        Action<TncScriptStep, KissTrafficKind, ReadOnlyMemory<byte>>? trace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        timeProvider ??= TimeProvider.System;
        var received = new StringBuilder();
        var matchedUpTo = 0;
        var buffer = new byte[256];
        foreach (var step in Steps)
        {
            switch (step.Kind)
            {
                case TncScriptStepKind.Send:
                    await stream.WriteAsync(step.Bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    trace?.Invoke(step, KissTrafficKind.SetupSent, step.Bytes);
                    break;
                case TncScriptStepKind.Pause:
                    await Task.Delay(step.Time, timeProvider, cancellationToken).ConfigureAwait(false);
                    break;
                case TncScriptStepKind.Drain:
                    await Task.Delay(step.Time, timeProvider, cancellationToken).ConfigureAwait(false);
                    (stream as IDiscardsInput)?.DiscardInput();
                    matchedUpTo = received.Length; // and nothing read already counts either
                    break;
                default:
                    using (var deadline = new CancellationTokenSource(step.Time, timeProvider))
                    using (
                        var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken)
                    )
                    {
                        int found;
                        while (
                            (
                                found = received
                                    .ToString(matchedUpTo, received.Length - matchedUpTo)
                                    .IndexOf(step.Text, StringComparison.OrdinalIgnoreCase)
                            ) < 0
                        )
                        {
                            int read;
                            try
                            {
                                read = await stream.ReadAsync(buffer, wait.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                trace?.Invoke(step, KissTrafficKind.SetupFailed, ReadOnlyMemory<byte>.Empty);
                                throw new TncScriptException(step, received.ToString(), closed: false);
                            }

                            if (read == 0)
                            {
                                trace?.Invoke(step, KissTrafficKind.SetupFailed, ReadOnlyMemory<byte>.Empty);
                                throw new TncScriptException(step, received.ToString(), closed: true);
                            }

                            trace?.Invoke(step, KissTrafficKind.SetupReceived, buffer.AsSpan(0, read).ToArray());
                            received.Append(Encoding.Latin1.GetString(buffer, 0, read));
                        }

                        matchedUpTo += found + step.Text.Length;
                    }

                    break;
            }
        }

        return received.ToString();
    }

    private static TncScriptStep Expect(string rest, int number)
    {
        var space = rest.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            throw new FormatException($"Line {number}: expect needs a time and some text, such as 'expect 3s cmd:'.");
        }

        return TncScriptStep.Expect(rest[(space + 1)..].Trim(), Duration(rest[..space], number));
    }

    private static TimeSpan Duration(string text, int number)
    {
        var (digits, scale) =
            text.EndsWith("ms", StringComparison.OrdinalIgnoreCase) ? (text[..^2], 1.0)
            : text.EndsWith('s') || text.EndsWith('S') ? (text[..^1], 1000.0)
            : (null, 0);
        return
            digits is not null
            && double.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? TimeSpan.FromMilliseconds(value * scale)
            : throw new FormatException($"Line {number}: '{text}' isn't a time such as 500ms or 3s.");
    }

    private static byte[] Hex(string text, int number)
    {
        var digits = text.Replace(" ", "", StringComparison.Ordinal);
        try
        {
            return digits.Length > 0
                ? Convert.FromHexString(digits)
                : throw new FormatException($"Line {number}: bytes needs hex bytes, such as 'bytes C0 FF C0'.");
        }
        catch (FormatException e) when (!e.Message.StartsWith("Line ", StringComparison.Ordinal))
        {
            throw new FormatException($"Line {number}: '{text}' isn't hex bytes, such as C0 FF C0.", e);
        }
    }
}

/// <summary>A <see cref="TncScript"/> that didn't get the answer it expected. A lost link, so a connection retries.</summary>
public sealed class TncScriptException : IOException
{
    internal TncScriptException(TncScriptStep step, string transcript, bool closed)
        : base(
            closed
                ? $"The TNC closed the port while the script waited for '{step.Text}'."
                : $"The TNC didn't send '{step.Text}' within {step.Time.TotalSeconds:0.###}s."
        )
    {
        Step = step;
        Transcript = transcript;
    }

    /// <summary>The step that failed.</summary>
    public TncScriptStep Step { get; }

    /// <summary>Everything the TNC sent while the script ran, as Latin-1 text.</summary>
    public string Transcript { get; }
}

/// <summary>
/// A port that can throw away what it has received but no one has read yet, without a read to cancel: a serial port
/// (its driver's receive buffer). Cancelling a serial read closes the port, so this is the only way to drop stale bytes.
/// </summary>
internal interface IDiscardsInput
{
    /// <summary>Throws away what was received and not yet read.</summary>
    void DiscardInput();
}
