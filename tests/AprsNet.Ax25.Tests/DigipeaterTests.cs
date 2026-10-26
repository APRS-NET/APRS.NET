using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Ax25.Tests;

/// <summary>
/// The New-N digipeating rules (APRS 1.1, fix14439), one path form per row, with the path a digipeater sends on. The
/// digipeater is N0DIG-1; TNC2 marks only the last repeated entry with <c>*</c>.
/// </summary>
public sealed class DigipeaterTests
{
    private static readonly Ax25Address _call = new("N0DIG", 1, flag: false);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private Digipeater Digi(
        bool fillInOnly = true,
        int maxHops = 2,
        bool preemptive = false,
        params string[] aliases
    ) =>
        new(
            _call,
            new DigipeaterOptions
            {
                FillInOnly = fillInOnly,
                MaxHops = maxHops,
                Preemptive = preemptive,
                Aliases = aliases,
                TimeProvider = _time,
            }
        );

    private static Ax25Frame Frame(string header) => Ax25Frame.FromAprsPacket(AprsPacket.Parse(header + ":>test"));

    private static string? Sent(DigipeatDecision decision) => decision.Frame?.ToTnc2()[..^":>test".Length];

    [Theory]
    // A fill-in digipeater repeats WIDE1-1 and nothing wider.
    [InlineData("N0CALL-9>APRS,WIDE1-1,WIDE2-1", "N0CALL-9>APRS,N0DIG-1*,WIDE2-1")]
    [InlineData("N0CALL-9>APRS,WIDE1-1", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,WIDE2-2", null)]
    [InlineData("N0CALL-9>APRS,DIGI1*,WIDE2-1", null)]
    // Addressed by call, SSID included.
    [InlineData("N0CALL-9>APRS,N0DIG-1", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,DIGI1*,N0DIG-1,DIGI3", "N0CALL-9>APRS,DIGI1,N0DIG-1*,DIGI3")]
    [InlineData("N0CALL-9>APRS,N0DIG", null)]
    public void AFillInDigipeater(string heard, string? sent)
    {
        Assert.Equal(sent, Sent(Digi().Digipeat(Frame(heard))));
    }

    [Theory]
    // New-N: count N down and insert the call ahead of the alias; the last hop replaces it.
    [InlineData("N0CALL-9>APRS,WIDE2-2", "N0CALL-9>APRS,N0DIG-1*,WIDE2-1")]
    [InlineData("N0CALL-9>APRS,DIGI1*,WIDE2-1", "N0CALL-9>APRS,DIGI1,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,WIDE1-1,WIDE2-1", "N0CALL-9>APRS,N0DIG-1*,WIDE2-1")]
    [InlineData("N0CALL-9>APRS,DIGI1*,WIDE2-1,WIDE3-3", "N0CALL-9>APRS,DIGI1,N0DIG-1*,WIDE3-3")]
    // Asking for more hops than allowed: trapped, repeated once as the last hop.
    [InlineData("N0CALL-9>APRS,WIDE3-3", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,WIDE7-7", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,WIDE2-5", "N0CALL-9>APRS,N0DIG-1*")]
    // Not WIDEn-N: other aliases, WIDE without an N, used-up entries.
    [InlineData("N0CALL-9>APRS,WIDE2", null)]
    [InlineData("N0CALL-9>APRS,RELAY", null)]
    [InlineData("N0CALL-9>APRS,TRACE2-2", null)]
    [InlineData("N0CALL-9>APRS,WIDE8-1", null)]
    [InlineData("N0CALL-9>APRS,DIGI1,WIDE2*", null)]
    public void AWideAreaDigipeater(string heard, string? sent)
    {
        Assert.Equal(sent, Sent(Digi(fillInOnly: false).Digipeat(Frame(heard))));
    }

    [Fact]
    public void AFullPathKeepsItsLengthAndStillCountsDown()
    {
        var heard = Frame("N0CALL-9>APRS,DIGI1,DIGI2,DIGI3,DIGI4,DIGI5,DIGI6,DIGI7*,WIDE2-2");

        Assert.Equal(
            "N0CALL-9>APRS,DIGI1,DIGI2,DIGI3,DIGI4,DIGI5,DIGI6,DIGI7*,WIDE2-1",
            Sent(Digi(fillInOnly: false).Digipeat(heard))
        );
    }

    [Theory]
    [InlineData(1, "N0CALL-9>APRS,WIDE2-2", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData(3, "N0CALL-9>APRS,WIDE3-3", "N0CALL-9>APRS,N0DIG-1*,WIDE3-2")]
    public void TheHopLimitComesFromTheOptions(int maxHops, string heard, string sent)
    {
        Assert.Equal(sent, Sent(Digi(fillInOnly: false, maxHops: maxHops).Digipeat(Frame(heard))));
    }

    [Theory]
    [InlineData("N0CALL-9>APRS,KCMO", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,DIGI1*,KCMO,WIDE2-1", "N0CALL-9>APRS,DIGI1,N0DIG-1*,WIDE2-1")]
    // Aliases are matched without regard to the case they were set in.
    public void AnAliasIsReplacedByTheCall(string heard, string sent)
    {
        Assert.Equal(sent, Sent(Digi(aliases: "kcmo").Digipeat(Frame(heard))));
    }

    [Theory]
    // The call or an alias later in the path, ahead of unused entries before it, which are dropped.
    [InlineData("N0CALL-9>APRS,DIGI1,N0DIG-1", "N0CALL-9>APRS,N0DIG-1*")]
    [InlineData("N0CALL-9>APRS,DIGI1*,DIGI2,KCMO,DIGI3", "N0CALL-9>APRS,DIGI1,N0DIG-1*,DIGI3")]
    [InlineData("N0CALL-9>APRS,DIGI2,WIDE1-1", "N0CALL-9>APRS,N0DIG-1*")]
    // Never for a generic WIDEn-N.
    [InlineData("N0CALL-9>APRS,DIGI2,WIDE2-2", null)]
    public void APreemptiveDigipeater(string heard, string? sent)
    {
        Assert.Equal(sent, Sent(Digi(preemptive: true, aliases: "KCMO").Digipeat(Frame(heard))));
        Assert.Null(Sent(Digi(aliases: "KCMO").Digipeat(Frame(heard))));
    }

    [Fact]
    public void EachRefusalHasAReason()
    {
        var digi = Digi();

        Assert.Equal(DigipeatRefusal.NoHopsLeft, digi.Digipeat(Frame("N0CALL-9>APRS")).Refusal);
        Assert.Equal(DigipeatRefusal.NoHopsLeft, digi.Digipeat(Frame("N0CALL-9>APRS,DIGI1,WIDE1*")).Refusal);
        Assert.Equal(DigipeatRefusal.NotForUs, digi.Digipeat(Frame("N0CALL-9>APRS,WIDE2-2")).Refusal);
        Assert.Equal(DigipeatRefusal.OwnFrame, digi.Digipeat(Frame("N0DIG-1>APRS,WIDE1-1")).Refusal);
        Assert.False(digi.Digipeat(Frame("N0DIG-1>APRS,WIDE1-1")).IsRepeated);
        Assert.Equal(DigipeatRefusal.NotAprs, digi.Digipeat(NotAprs()).Refusal);
    }

    [Fact]
    public void ACopyOfARepeatedFrameIsRefusedWithinTheWindow()
    {
        var digi = Digi();
        Assert.True(digi.Digipeat(Frame("N0CALL-9>APRS,WIDE1-1,WIDE2-1")).IsRepeated);

        // The same frame heard again through another fill-in digipeater, by another path.
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(DigipeatRefusal.Duplicate, digi.Digipeat(Frame("N0CALL-9>APRS,WIDE1-1")).Refusal);

        _time.Advance(DuplicateFilter.DefaultWindow);
        Assert.True(digi.Digipeat(Frame("N0CALL-9>APRS,WIDE1-1")).IsRepeated);
    }

    [Fact]
    public void ARefusedFrameDoesNotBlockALaterCopyMeantForIt()
    {
        var digi = Digi();
        Assert.False(digi.Digipeat(Frame("N0CALL-9>APRS,WIDE2-2")).IsRepeated);

        Assert.True(digi.Digipeat(Frame("N0CALL-9>APRS,WIDE1-1")).IsRepeated);
    }

    [Fact]
    public void AFrameAddressedByCallIsRepeatedEvenAsACopy()
    {
        var digi = Digi();
        Assert.True(digi.Digipeat(Frame("N0CALL-9>APRS,WIDE1-1")).IsRepeated);

        Assert.True(digi.Digipeat(Frame("N0CALL-9>APRS,N0DIG-1")).IsRepeated);
    }

    [Fact]
    public void TheInformationFieldIsSentUnchanged()
    {
        var heard = Ax25Frame.FromAprsPacket(AprsPacket.Parse("N0CALL-9>S32U6T,WIDE1-1,WIDE2-1:`(_fn\"Oj/]Mic-E"));

        var sent = Digi().Digipeat(heard).Frame!;

        Assert.Equal(heard.Information.ToArray(), sent.Information.ToArray());
        Assert.Equal(heard.Destination, sent.Destination);
        Assert.Equal(heard.Source, sent.Source);
        Assert.True(sent.Digipeaters[0].Flag);
        Assert.False(sent.Digipeaters[1].Flag);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void UnusableSettingsAreRefused(int maxHops)
    {
        Assert.ThrowsAny<ArgumentException>(() => Digi(maxHops: maxHops));
        Assert.ThrowsAny<ArgumentException>(() => Digi(aliases: "not an alias"));
        Assert.Throws<ArgumentException>(() => new Digipeater(default));
    }

    private static Ax25Frame NotAprs()
    {
        var bytes = Frame("N0CALL-9>APRS,WIDE1-1").GetBytes();
        bytes[(3 * 7) + 1] = 0xCF; // NET/ROM, not "no layer 3"
        Assert.True(Ax25Frame.TryDecode(bytes, out var frame, out _));
        return frame!;
    }
}
