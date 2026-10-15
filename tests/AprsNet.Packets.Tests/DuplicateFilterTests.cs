using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Packets.Tests;

public sealed class DuplicateFilterTests
{
    private readonly FakeTimeProvider _time = new();

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    [Fact]
    public void TheSamePacketByAnotherPathIsADuplicate()
    {
        var dedupe = new DuplicateFilter(timeProvider: _time);
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
        var dedupe = new DuplicateFilter(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("N0CALL>APRS:>hello")));
        Assert.True(dedupe.TryAccept(P(other)));
    }

    [Fact]
    public void TheWindowRunsFromTheFirstCopy()
    {
        var dedupe = new DuplicateFilter(timeProvider: _time);
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
        var dedupe = new DuplicateFilter(TimeSpan.FromSeconds(5), timeProvider: _time);
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
        var dedupe = new DuplicateFilter(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("no header here")));
        Assert.False(dedupe.TryAccept(P("no header here")));
        Assert.True(dedupe.TryAccept(P("no header there")));
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new DuplicateFilter().Window);
        Assert.Equal(DuplicateFilter.DefaultCapacity, new DuplicateFilter().Capacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuplicateFilter(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DuplicateFilter(capacity: 0));
        Assert.Throws<ArgumentNullException>(() => new DuplicateFilter().TryAccept(null!));
        Assert.Throws<ArgumentNullException>(() => new DuplicateFilter().IsDuplicate(null!));
    }

    [Fact]
    public void MicEDestinationsArePartOfTheIdentity()
    {
        // The destination holds the latitude, so the same information field from another place is another packet.
        var dedupe = new DuplicateFilter(timeProvider: _time);
        Assert.True(dedupe.TryAccept(P("N0CALL-9>S32UVT,WIDE1-1,qAR,IGATE1:`(_fn\"Oj/]")));
        Assert.True(dedupe.TryAccept(P("N0CALL-9>S32UVU,WIDE1-1,qAR,IGATE1:`(_fn\"Oj/]")));
        Assert.False(dedupe.TryAccept(P("N0CALL-9>S32UVT,qAO,IGATE2:`(_fn\"Oj/]")));
    }

    [Fact]
    public void IsDuplicateDoesNotRemember()
    {
        var dedupe = new DuplicateFilter(timeProvider: _time);
        var packet = P("N0CALL>APRS:>hello");
        Assert.False(dedupe.IsDuplicate(packet));
        Assert.False(dedupe.IsDuplicate(packet));
        Assert.True(dedupe.TryAccept(packet));
        Assert.True(dedupe.IsDuplicate(P("N0CALL>APRS,qAR,IGATE:>hello")));
        _time.Advance(DuplicateFilter.DefaultWindow);
        Assert.False(dedupe.IsDuplicate(packet));
    }

    [Fact]
    public void AFloodForgetsTheOldestPacketsFirst()
    {
        var dedupe = new DuplicateFilter(capacity: 1_000, timeProvider: _time);
        for (var i = 0; i < 50_000; i++)
        {
            Assert.True(dedupe.TryAccept(P($"N0CALL>APRS:>flood {i}")));
            Assert.True(dedupe.Count <= 1_000);
        }

        Assert.Equal(1_000, dedupe.Count);
        Assert.True(dedupe.IsDuplicate(P("N0CALL>APRS:>flood 49999")));
        Assert.True(dedupe.IsDuplicate(P("N0CALL>APRS:>flood 49000")));
        Assert.False(dedupe.IsDuplicate(P("N0CALL>APRS:>flood 48999")));
    }

    [Fact]
    public void IdentitiesIgnoreThePath()
    {
        var identity = PacketIdentity.Of(P("N0CALL>APRS,WIDE1-1,qAR,IGATE:>hello"));

        Assert.Equal("N0CALL>APRS:>hello", identity.Value);
        Assert.Equal(identity, PacketIdentity.Of(P("N0CALL>APRS:>hello")));
        Assert.Equal("N0CALL>APRS:>hello", identity.ToString());
        Assert.Equal("", default(PacketIdentity).Value);
        Assert.Throws<ArgumentNullException>(() => PacketIdentity.Of(null!));
    }
}
