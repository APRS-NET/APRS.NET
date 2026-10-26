using System.Globalization;
using AprsNet.Packets;

namespace AprsNet.Ax25;

/// <summary>
/// Decides which frames heard on RF an APRS digipeater repeats, and rewrites their path, under the New-N paradigm
/// (APRS 1.1, fix14439) as Dire Wolf does. It does no I/O: pass it each frame the TNC hears, and send the frames it
/// returns. Safe to use from several threads.
/// </summary>
/// <remarks>
/// <para>
/// Only the first digipeater not yet marked as repeated counts. It is repeated when it is:
/// </para>
/// <list type="bullet">
/// <item>the digipeater's own call, SSID included: replaced by it and marked repeated;</item>
/// <item>
/// one of its <see cref="DigipeaterOptions.Aliases"/>, or <c>WIDE1-1</c> for a fill-in digipeater: replaced by its call
/// and marked repeated;
/// </item>
/// <item>
/// with <see cref="DigipeaterOptions.FillInOnly"/> off, a <c>WIDEn-N</c> alias: <c>N</c> is counted down and the
/// digipeater's call inserted before it, marked repeated, so the path traces the frame (<c>WIDE2-2</c> becomes
/// <c>DIGI*,WIDE2-1</c>); the last hop replaces the alias (<c>WIDE2-1</c> becomes <c>DIGI*</c>). A <c>WIDEn-N</c>
/// asking for more than <see cref="DigipeaterOptions.MaxHops"/> is trapped: repeated once as the last hop, so it goes no
/// further.
/// </item>
/// </list>
/// <para>
/// With <see cref="DigipeaterOptions.Preemptive"/>, the call or an alias later in the path is also taken, and the unused
/// entries before it are dropped. Frames from the digipeater's own call are never repeated, nor copies of a frame it
/// repeated within <see cref="DigipeaterOptions.DuplicateWindow"/> (the same source, destination and information field,
/// whatever the path). A frame addressed to the digipeater by its call is always repeated, for testing a path.
/// </para>
/// </remarks>
public sealed class Digipeater
{
    private readonly DuplicateFilter _repeated;
    private readonly HashSet<Ax25Address> _aliases;
    private readonly bool _fillInOnly;
    private readonly int _maxHops;
    private readonly bool _preemptive;

    /// <summary>Creates the rules for a digipeater that transmits as <paramref name="call"/>.</summary>
    /// <param name="call">The digipeater's own call, as it is inserted in the paths it repeats.</param>
    /// <param name="options">Its aliases, mode, limits and clock; the defaults suit a fill-in digipeater.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="call"/> is <see langword="default"/>, or a setting in <paramref name="options"/> is unusable.
    /// </exception>
    public Digipeater(Ax25Address call, DigipeaterOptions? options = null)
    {
        if (call.Callsign.Length == 0)
        {
            throw new ArgumentException("The digipeater needs a call.", nameof(call));
        }

        options ??= new DigipeaterOptions();
        options.Validate();
        Call = new Ax25Address(call.Callsign, call.Ssid, flag: false);
        _fillInOnly = options.FillInOnly;
        _maxHops = options.MaxHops;
        _preemptive = options.Preemptive;
        _aliases = [.. options.Aliases.Select(Alias)];
        if (_fillInOnly)
        {
            _aliases.Add(new Ax25Address("WIDE1", 1, flag: false));
        }

        _repeated = new DuplicateFilter(options.DuplicateWindow, timeProvider: options.TimeProvider);
    }

    /// <summary>The digipeater's own call.</summary>
    public Ax25Address Call { get; }

    /// <summary>Decides whether to repeat <paramref name="heard"/>, and builds the frame to send if so.</summary>
    /// <param name="heard">A frame as the TNC heard it.</param>
    public DigipeatDecision Digipeat(Ax25Frame heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        if (!heard.IsAprs)
        {
            return Refuse(heard, DigipeatRefusal.NotAprs);
        }

        var path = heard.Digipeaters;
        var next = -1;
        for (var i = 0; i < path.Count; i++)
        {
            if (!path[i].Flag)
            {
                next = i;
                break;
            }
        }

        if (next < 0)
        {
            return Refuse(heard, DigipeatRefusal.NoHopsLeft);
        }

        // Addressed by call: always, for testing a path.
        if (Same(path[next], Call))
        {
            return Repeat(heard, Replace(path, next));
        }

        if (Same(heard.Source, Call))
        {
            return Refuse(heard, DigipeatRefusal.OwnFrame);
        }

        var digipeaters = Match(path, next);
        if (digipeaters is null)
        {
            return Refuse(heard, DigipeatRefusal.NotForUs);
        }

        // Remembered only once repeated, so a copy refused here doesn't block a later one meant for us.
        return _repeated.Check(heard.ToAprsPacket()).IsDuplicate
            ? Refuse(heard, DigipeatRefusal.Duplicate)
            : Repeat(heard, digipeaters);
    }

    private List<Ax25Address>? Match(IReadOnlyList<Ax25Address> path, int next)
    {
        if (_aliases.Contains(Unflagged(path[next])))
        {
            return Replace(path, next);
        }

        if (_preemptive)
        {
            for (var later = next + 1; later < path.Count; later++)
            {
                var entry = Unflagged(path[later]);
                if (Same(entry, Call) || _aliases.Contains(entry))
                {
                    // The unused entries before it are dropped, so the path shows where the frame went.
                    var preempted = Replace(path, later);
                    preempted.RemoveRange(next, later - next);
                    return preempted;
                }
            }
        }

        if (_fillInOnly || Wide(path[next]) is not { } wide)
        {
            return null;
        }

        var (n, hops) = wide;

        if (hops == 1 || n > _maxHops || hops > _maxHops)
        {
            return Replace(path, next);
        }

        var traced = path.ToList();
        traced[next] = new Ax25Address(path[next].Callsign, hops - 1, flag: false);
        if (traced.Count < Ax25Frame.MaxDigipeaters)
        {
            traced.Insert(next, Mark(Call));
        }

        return traced;
    }

    /// <summary>The <c>n</c> and remaining <c>N</c> of a <c>WIDEn-N</c> alias, both 1 to 7.</summary>
    private static (int N, int Hops)? Wide(Ax25Address entry) =>
        entry.Callsign.Length == 5
        && entry.Callsign.StartsWith("WIDE", StringComparison.Ordinal)
        && entry.Callsign[4] is >= '1' and <= '7'
        && entry.Ssid is >= 1 and <= 7
            ? (entry.Callsign[4] - '0', entry.Ssid)
            : null;

    private List<Ax25Address> Replace(IReadOnlyList<Ax25Address> path, int at)
    {
        var replaced = path.ToList();
        replaced[at] = Mark(Call);
        return replaced;
    }

    private static Ax25Address Mark(Ax25Address address) => new(address.Callsign, address.Ssid, flag: true);

    private static Ax25Address Unflagged(Ax25Address address) => new(address.Callsign, address.Ssid, flag: false);

    private static bool Same(Ax25Address a, Ax25Address b) => a.Callsign == b.Callsign && a.Ssid == b.Ssid;

    private static Ax25Address Alias(string alias) =>
        Ax25Address.TryParse(alias.ToUpperInvariant(), out var address)
            ? new Ax25Address(address.Callsign, address.Ssid, flag: false)
            : throw new ArgumentException($"The alias '{alias}' isn't an AX.25 address.", nameof(alias));

    private static DigipeatDecision Repeat(Ax25Frame heard, List<Ax25Address> digipeaters) =>
        new(heard, heard.WithDigipeaters(digipeaters), null);

    private static DigipeatDecision Refuse(Ax25Frame heard, DigipeatRefusal refusal) => new(heard, null, refusal);
}

/// <summary>Settings for a <see cref="Digipeater"/>.</summary>
public sealed class DigipeaterOptions
{
    /// <summary>
    /// Whether the digipeater is a fill-in one, repeating only <c>WIDE1-1</c>, its call and its aliases:
    /// <see langword="true"/> by default, for a station filling a gap under wide-area digipeaters. Turn it off only
    /// for a wide-area digipeater, by agreement with the local network: it then repeats <c>WIDEn-N</c> too.
    /// </summary>
    public bool FillInOnly { get; set; } = true;

    /// <summary>
    /// Other names the digipeater answers to, such as a location name, each replaced by its call when it is next in a
    /// path. None by default.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; set; } = [];

    /// <summary>
    /// The most hops a <c>WIDEn-N</c> may ask for, 1 to 7: 2 by default, as APRS 1.1 recommends for most areas. One that
    /// asks for more, such as <c>WIDE3-3</c> or <c>WIDE7-7</c>, is trapped: repeated once, and no further.
    /// </summary>
    public int MaxHops { get; set; } = 2;

    /// <summary>
    /// Whether to take the digipeater's call or an alias later in the path, ahead of unused entries before it, which
    /// are dropped. Off by default; APRS 1.1 advises against it for generic <c>WIDEn-N</c> paths, which it never
    /// applies to.
    /// </summary>
    public bool Preemptive { get; set; }

    /// <summary>
    /// How long a repeated frame's copies are refused: 30 seconds by default. A frame heard again from another
    /// digipeater goes out once.
    /// </summary>
    public TimeSpan DuplicateWindow { get; set; } = DuplicateFilter.DefaultWindow;

    /// <summary>The clock for the duplicate window. The system clock by default.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Checks that the settings can be used.</summary>
    /// <exception cref="ArgumentException">A setting is unusable; the message names it.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Aliases, nameof(Aliases));
        foreach (var alias in Aliases)
        {
            if (alias is null || !Ax25Address.TryParse(alias.ToUpperInvariant(), out _))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The alias '{alias}' isn't an AX.25 address."),
                    nameof(Aliases)
                );
            }
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxHops, 1, nameof(MaxHops));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxHops, 7, nameof(MaxHops));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DuplicateWindow, TimeSpan.Zero, nameof(DuplicateWindow));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
    }
}

/// <summary>What a <see cref="Digipeater"/> decided for a frame.</summary>
/// <param name="Heard">The frame as heard.</param>
/// <param name="Frame">The frame to send, with its new path, or <see langword="null"/> when it isn't repeated.</param>
/// <param name="Refusal">Why it isn't repeated, or <see langword="null"/> when it is.</param>
public sealed record DigipeatDecision(Ax25Frame Heard, Ax25Frame? Frame, DigipeatRefusal? Refusal)
{
    /// <summary>Whether the frame is repeated: <see cref="Frame"/> is the one to send.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Frame))]
    public bool IsRepeated => Frame is not null;
}

/// <summary>
/// Why a <see cref="Digipeater"/> didn't repeat a frame. A stable vocabulary, also used as metric and log values:
/// members may be added but are never renamed or reused.
/// </summary>
public enum DigipeatRefusal
{
    /// <summary>The frame isn't APRS: not a UI frame, or a layer 3 protocol other than none.</summary>
    NotAprs,

    /// <summary>No digipeater left in the path that hasn't repeated it: none asked for, or all used.</summary>
    NoHopsLeft,

    /// <summary>The next hop is neither the digipeater's call nor anything it answers to.</summary>
    NotForUs,

    /// <summary>The digipeater's own frame, heard back from another.</summary>
    OwnFrame,

    /// <summary>A copy of a frame it repeated within the duplicate window.</summary>
    Duplicate,
}
