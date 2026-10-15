using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class PacketDeduplicatorTests
{
    private readonly FakeTimeProvider _time = new();

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    [Fact]
    public void TheSamePacketByAnotherPathIsADuplicate()
    {
        var dedupe = new PacketDeduplicator(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("N0CALL>APRS,WIDE1-1,qAR,IGATE1:>hello")));
        Assert.False(dedupe.TryAccept(P("N0CALL>APRS,DIGI*,WIDE2-1,qAR,IGATE2:>hello")));
        Assert.False(dedupe.TryAccept(P("N0CALL>APRS:>hello")));
        Assert.Equal(1, dedupe.Count);
    }

    [Theory]
    [InlineData("N0CALL-1>APRS:>hello")]
    [InlineData("N0CALL>APRS-1:>hello")]
    [InlineData("N0CALL>APRS:>hello!")]
    [InlineData("n0call>APRS:>hello")]
    public void AnyOtherDifferenceIsANewPacket(string other)
    {
        var dedupe = new PacketDeduplicator(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("N0CALL>APRS:>hello")));
        Assert.True(dedupe.TryAccept(P(other)));
    }

    [Fact]
    public void TheWindowRunsFromTheFirstCopy()
    {
        var dedupe = new PacketDeduplicator(timeProvider: _time);
        var packet = P("N0CALL>APRS:>beacon");
        Assert.True(dedupe.TryAccept(packet));
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(dedupe.TryAccept(packet));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(dedupe.TryAccept(packet));
    }

    [Fact]
    public void ExpiredPacketsAreForgotten()
    {
        var dedupe = new PacketDeduplicator(TimeSpan.FromSeconds(5), _time);
        Assert.Equal(TimeSpan.FromSeconds(5), dedupe.Window);
        dedupe.TryAccept(P("N0CALL>APRS:>one"));
        _time.Advance(TimeSpan.FromSeconds(3));
        dedupe.TryAccept(P("N0CALL>APRS:>two"));
        Assert.Equal(2, dedupe.Count);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, dedupe.Count);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, dedupe.Count);
    }

    [Fact]
    public void LinesWithoutAHeaderAreComparedWhole()
    {
        var dedupe = new PacketDeduplicator(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("no header here")));
        Assert.False(dedupe.TryAccept(P("no header here")));
        Assert.True(dedupe.TryAccept(P("no header there")));
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new PacketDeduplicator().Window);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketDeduplicator(TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new PacketDeduplicator().TryAccept(null!));
    }
}
