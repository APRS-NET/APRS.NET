namespace AprsNet.Packets;

/// <summary>
/// A device or program that sends APRS packets, as the aprs-deviceid list describes it. Any part the list leaves out
/// is <see langword="null"/>.
/// </summary>
public sealed record AprsDevice
{
    /// <summary>The maker or author, such as <c>Kenwood</c>.</summary>
    public string? Vendor { get; init; }

    /// <summary>The model or program, such as <c>TH-D74</c>.</summary>
    public string? Model { get; init; }

    /// <summary>What kind of device it is.</summary>
    public AprsDeviceClass? Class { get; init; }

    /// <summary>The operating system it runs on, as the list writes it, such as <c>Android</c> or <c>embedded</c>.</summary>
    public string? OperatingSystem { get; init; }

    /// <summary>APRS features the list says it supports.</summary>
    public AprsDeviceFeatures Features { get; init; }

    /// <summary>The vendor and model, such as <c>Kenwood TH-D74</c>.</summary>
    public override string ToString() => string.Join(' ', new[] { Vendor, Model }.Where(p => p is not null));
}

/// <summary>What kind of device an <see cref="AprsDevice"/> is: the aprs-deviceid classes.</summary>
public enum AprsDeviceClass
{
    /// <summary>A dedicated weather station (<c>wx</c>).</summary>
    Weather,

    /// <summary>A tracker (<c>tracker</c>).</summary>
    Tracker,

    /// <summary>A small device that is not a tracker (<c>gadget</c>).</summary>
    Gadget,

    /// <summary>A mobile or desktop radio (<c>rig</c>).</summary>
    Rig,

    /// <summary>A hand-held radio (<c>ht</c>).</summary>
    HandHeld,

    /// <summary>A phone or tablet app (<c>app</c>).</summary>
    App,

    /// <summary>Desktop software (<c>software</c>).</summary>
    Software,

    /// <summary>Software with no user interface (<c>daemon</c>).</summary>
    Daemon,

    /// <summary>A web service or message bot (<c>service</c>).</summary>
    Service,

    /// <summary>A network appliance with its own APRS features (<c>network</c>).</summary>
    Network,

    /// <summary>Digipeater software (<c>digi</c>).</summary>
    Digipeater,

    /// <summary>IGate software (<c>igate</c>).</summary>
    IGate,

    /// <summary>A D-STAR radio (<c>dstar</c>).</summary>
    DStar,

    /// <summary>A station on a satellite (<c>satellite</c>).</summary>
    Satellite,
}

/// <summary>APRS features an <see cref="AprsDevice"/> supports, from the aprs-deviceid list.</summary>
[Flags]
public enum AprsDeviceFeatures
{
    /// <summary>None listed.</summary>
    None = 0,

    /// <summary>Text messages (listed for legacy Mic-E radios, which cannot say so in their packets).</summary>
    Messaging = 1,

    /// <summary>Items embedded in text messages.</summary>
    ItemInMessage = 2,
}
