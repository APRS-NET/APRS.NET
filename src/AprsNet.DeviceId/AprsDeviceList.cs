using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AprsNet.Packets;

namespace AprsNet.DeviceId;

/// <summary>
/// An aprs-deviceid list (<see href="https://github.com/aprsorg/aprs-deviceid"/>) to identify devices by: the one built
/// into this package (<see cref="BuiltIn"/>), or a newer one an app has loaded (<see cref="Parse(Stream)"/>), say from
/// <see cref="AprsDeviceListSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// A loaded list is read under the same rules as the built-in one, so its lookups match what the built-in table would
/// say for the same file. In TOCALL patterns <c>?</c> stands for any character, <c>n</c> for a digit and a final
/// <c>*</c> for any characters; when several match, the one with the most literal characters wins, then one without a
/// <c>*</c>.
/// </para>
/// <para>
/// The list's data is licensed under CC BY-SA 2.0 by its maintainers, Heikki Hannikainen, OH7LZB, and contributors. A
/// list is immutable and thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var list = File.Exists(path) &amp;&amp; AprsDeviceList.TryParse(File.OpenRead(path), out var saved, out _)
///     &amp;&amp; saved.IsNewerThan(AprsDeviceList.BuiltIn) ? saved : AprsDeviceList.BuiltIn;
/// var device = list.Identify(packet);
/// </code>
/// </example>
public sealed class AprsDeviceList
{
    private static readonly Dictionary<string, AprsDeviceClass> _classes = new(StringComparer.Ordinal)
    {
        ["wx"] = AprsDeviceClass.Weather,
        ["tracker"] = AprsDeviceClass.Tracker,
        ["gadget"] = AprsDeviceClass.Gadget,
        ["rig"] = AprsDeviceClass.Rig,
        ["ht"] = AprsDeviceClass.HandHeld,
        ["app"] = AprsDeviceClass.App,
        ["software"] = AprsDeviceClass.Software,
        ["daemon"] = AprsDeviceClass.Daemon,
        ["service"] = AprsDeviceClass.Service,
        ["network"] = AprsDeviceClass.Network,
        ["digi"] = AprsDeviceClass.Digipeater,
        ["igate"] = AprsDeviceClass.IGate,
        ["dstar"] = AprsDeviceClass.DStar,
        ["satellite"] = AprsDeviceClass.Satellite,
    };

    private static readonly Dictionary<string, AprsDeviceFeatures> _features = new(StringComparer.Ordinal)
    {
        ["messaging"] = AprsDeviceFeatures.Messaging,
        ["item-in-msg"] = AprsDeviceFeatures.ItemInMessage,
    };

    private readonly Dictionary<string, AprsDevice> _tocalls;
    private readonly (string Pattern, AprsDevice Device)[] _tocallPatterns;
    private readonly Dictionary<string, AprsDevice> _micE;
    private readonly (char Prefix, char? Suffix, AprsDevice Device)[] _micELegacy;

    internal AprsDeviceList(
        Dictionary<string, AprsDevice> tocalls,
        (string Pattern, AprsDevice Device)[] tocallPatterns,
        Dictionary<string, AprsDevice> micE,
        (char Prefix, char? Suffix, AprsDevice Device)[] micELegacy,
        DateTimeOffset? generatedAt,
        string? commit
    )
    {
        _tocalls = tocalls;
        _tocallPatterns = tocallPatterns;
        _micE = micE;
        _micELegacy = micELegacy;
        GeneratedAt = generatedAt;
        Commit = commit;
    }

    /// <summary>The list built into this package, which <see cref="AprsDevices"/> uses.</summary>
    public static AprsDeviceList BuiltIn => AprsDevices.BuiltInList;

    /// <summary>
    /// When the list was generated: the published file's <c>meta.generation_time</c>, or <see langword="null"/> when a
    /// file has none. <see cref="IsNewerThan"/> compares it.
    /// </summary>
    public DateTimeOffset? GeneratedAt { get; }

    /// <summary>
    /// The aprs-deviceid commit the list came from, when known: for <see cref="BuiltIn"/>; the published file doesn't
    /// say.
    /// </summary>
    public string? Commit { get; }

    /// <summary>How many TOCALLs and TOCALL patterns the list has.</summary>
    public int TocallCount => _tocalls.Count + _tocallPatterns.Length;

    /// <summary>
    /// Whether this list was generated after <paramref name="other"/>, by <see cref="GeneratedAt"/>. A list whose time
    /// isn't known is never newer, and any list with a time is newer than one without.
    /// </summary>
    public bool IsNewerThan(AprsDeviceList other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return GeneratedAt is { } mine && (other.GeneratedAt is not { } theirs || mine > theirs);
    }

    /// <summary>Reads a list as published: <c>tocalls.pretty.json</c> or its compact form, <c>tocalls.dense.json</c>.</summary>
    /// <exception cref="InvalidDataException">
    /// The file isn't a list this version can read; the message says why (not JSON, a missing section, an unknown
    /// device class or feature, a TOCALL pattern it can't match, no TOCALLs …).
    /// </exception>
    public static AprsDeviceList Parse(Stream json) =>
        TryParse(json, out var list, out var reason) ? list : throw new InvalidDataException(reason);

    /// <inheritdoc cref="Parse(Stream)"/>
    public static AprsDeviceList Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Reads a list as published, or says why it can't: see <see cref="Parse(Stream)"/>.</summary>
    /// <param name="json">The file.</param>
    /// <param name="list">The list, when it could be read.</param>
    /// <param name="reason">Why it couldn't, when it couldn't.</param>
    public static bool TryParse(
        Stream json,
        [NotNullWhen(true)] out AprsDeviceList? list,
        [NotNullWhen(false)] out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(json);
        list = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            reason = Read(document.RootElement, out list);
        }
        catch (JsonException e)
        {
            reason = $"The file isn't JSON: {e.Message}";
        }

        return list is not null;
    }

    /// <summary>
    /// The device that sent <paramref name="packet"/>: for Mic-E, the one its device codes name (the destination holds
    /// the latitude there); otherwise the one its destination names.
    /// </summary>
    /// <remarks>
    /// Mic-E devices are read from the raw information field: a legacy Kenwood code is the type byte <c>&gt;</c> or
    /// <c>]</c> and, for some models, a final character; a newer code is the type byte <c>`</c> or <c>'</c> and the
    /// final two characters. <see cref="MicEReport.Device"/> knows only the codes in the APRS 1.2 specification.
    /// </remarks>
    public AprsDevice? Identify(AprsPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Type == PacketType.MicE ? FromMicE(packet.Information.Span)
            : packet.Destination is { } destination ? FromTocall(destination.Value)
            : null;
    }

    /// <summary>
    /// The device a destination such as <c>APDW17</c> names, or <see langword="null"/> when the list has none. An SSID
    /// (<c>APDW17-1</c>) is ignored.
    /// </summary>
    public AprsDevice? FromTocall(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var dash = destination.IndexOf('-', StringComparison.Ordinal);
        var tocall = dash < 0 ? destination : destination[..dash];
        if (_tocalls.TryGetValue(tocall, out var device))
        {
            return device;
        }

        foreach (var (pattern, candidate) in _tocallPatterns)
        {
            if (Matches(pattern, tocall))
            {
                return candidate;
            }
        }

        return null;
    }

    // A Mic-E information field: data type, 6 bytes of longitude, speed and course, the symbol and its table, then the
    // optional type byte and the status text, with any device code at its end.
    private AprsDevice? FromMicE(ReadOnlySpan<byte> info)
    {
        if (info.Length < 10)
        {
            return null;
        }

        var text = info[10..];
        switch ((char)info[9])
        {
            case '>' or ']':
                var last = text.IsEmpty ? (char?)null : (char)text[^1];
                foreach (var (prefix, suffix, device) in _micELegacy)
                {
                    if (prefix == info[9] && (suffix is null || suffix == last))
                    {
                        return device;
                    }
                }

                return null;
            case '`' or '\'' when text.Length >= 2:
                return _micE.GetValueOrDefault(Encoding.Latin1.GetString(text[^2..]));
            default:
                return null;
        }
    }

    private static bool Matches(string pattern, string tocall)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*')
            {
                return true;
            }

            if (i == tocall.Length)
            {
                return false;
            }

            var ok = pattern[i] switch
            {
                '?' => true,
                'n' => char.IsAsciiDigit(tocall[i]),
                var literal => literal == tocall[i],
            };
            if (!ok)
            {
                return false;
            }
        }

        return pattern.Length == tocall.Length;
    }

    /// <summary>
    /// The order patterns are tried in, the most specific first: more literal characters, then a fixed length before a
    /// trailing <c>*</c>, then ordinal order, so the result never depends on the file's order. The generator uses the
    /// same order for the built-in list.
    /// </summary>
    internal static int ComparePatterns(string a, string b) =>
        Literals(b).CompareTo(Literals(a)) is not 0 and var byLiterals ? byLiterals
        : a.EndsWith('*').CompareTo(b.EndsWith('*')) is not 0 and var byStar ? byStar
        : string.CompareOrdinal(a, b);

    private static int Literals(string pattern) => pattern.Count(ch => ch is not ('n' or '?' or '*'));

    // Reads the list's sections; returns why it can't, or null with the list.
    private static string? Read(JsonElement root, out AprsDeviceList? list)
    {
        list = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "The file isn't a JSON object.";
        }

        foreach (var name in new[] { "classes", "tocalls", "mice", "micelegacy" })
        {
            if (!root.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                return $"The file has no '{name}' section.";
            }
        }

        foreach (var cls in root.GetProperty("classes").EnumerateObject())
        {
            if (!_classes.ContainsKey(cls.Name))
            {
                return $"Unknown device class '{cls.Name}': this version of AprsNet.DeviceId doesn't know it.";
            }
        }

        var tocalls = new Dictionary<string, AprsDevice>(StringComparer.Ordinal);
        var patterns = new List<(string Pattern, AprsDevice Device)>();
        foreach (var entry in root.GetProperty("tocalls").EnumerateObject())
        {
            var tocall = entry.Name;
            if (
                tocall.Length == 0
                || tocall.IndexOf('*', StringComparison.Ordinal) is var star and >= 0 && star != tocall.Length - 1
            )
            {
                return $"TOCALL '{tocall}': '*' is only supported at the end.";
            }

            if (tocall.Any(ch => ch is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or 'n' or '?' or '*')))
            {
                return $"TOCALL '{tocall}' has a character other than A-Z, 0-9, n, ? or *.";
            }

            if (Device(entry.Value, out var device) is { } bad)
            {
                return $"TOCALL '{tocall}': {bad}";
            }

            if (tocall.Any(ch => ch is 'n' or '?' or '*'))
            {
                patterns.Add((tocall, device!));
            }
            else
            {
                tocalls[tocall] = device!;
            }
        }

        if (tocalls.Count + patterns.Count == 0)
        {
            return "The file has no TOCALLs.";
        }

        patterns.Sort((a, b) => ComparePatterns(a.Pattern, b.Pattern));

        var micE = new Dictionary<string, AprsDevice>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("mice").EnumerateObject())
        {
            if (entry.Name.Length != 2)
            {
                return $"Mic-E code '{entry.Name}' is not 2 characters.";
            }

            if (Device(entry.Value, out var device) is { } bad)
            {
                return $"Mic-E code '{entry.Name}': {bad}";
            }

            micE[entry.Name] = device!;
        }

        var legacy = new List<(char Prefix, char? Suffix, AprsDevice Device)>();
        foreach (
            var entry in root.GetProperty("micelegacy").EnumerateObject().OrderBy(e => e.Name, StringComparer.Ordinal)
        )
        {
            var prefix = Text(entry.Value, "prefix");
            var suffix = Text(entry.Value, "suffix");
            if (prefix is not { Length: 1 } || suffix is { Length: not 1 })
            {
                return $"Legacy Mic-E code '{entry.Name}': its prefix or suffix is not 1 character.";
            }

            if (Device(entry.Value, out var device) is { } bad)
            {
                return $"Legacy Mic-E code '{entry.Name}': {bad}";
            }

            legacy.Add((prefix[0], suffix?[0], device!));
        }

        // Those with a suffix come before the bare prefix, so the suffix is checked first.
        legacy = [.. legacy.OrderBy(l => l.Suffix is null ? 1 : 0)];

        var generatedAt =
            root.TryGetProperty("meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && Text(meta, "generation_time") is { } time
            && DateTimeOffset.TryParse(
                time,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
                ? parsed
                : (DateTimeOffset?)null;

        list = new AprsDeviceList(tocalls, [.. patterns], micE, [.. legacy], generatedAt, commit: null);
        return null;
    }

    // Reads an entry's device; returns why it can't, or null with the device.
    private static string? Device(JsonElement entry, out AprsDevice? device)
    {
        device = null;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return "the entry isn't an object.";
        }

        AprsDeviceClass? cls = null;
        if (Text(entry, "class") is { } name)
        {
            if (!_classes.TryGetValue(name, out var known))
            {
                return $"unknown device class '{name}'.";
            }

            cls = known;
        }

        var features = AprsDeviceFeatures.None;
        if (entry.TryGetProperty("features", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in list.EnumerateArray())
            {
                if (
                    feature.ValueKind != JsonValueKind.String
                    || !_features.TryGetValue(feature.GetString()!, out var flag)
                )
                {
                    return $"unknown feature {feature.GetRawText()}.";
                }

                features |= flag;
            }
        }

        device = new AprsDevice
        {
            Vendor = Text(entry, "vendor"),
            Model = Text(entry, "model"),
            Class = cls,
            OperatingSystem = Text(entry, "os"),
            Features = features,
        };
        return null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
