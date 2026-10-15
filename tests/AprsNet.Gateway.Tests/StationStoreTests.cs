using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Gateway.Tests;

public sealed class StationStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));

    private static AprsPacket P(string line) => AprsPacket.Parse(line);

    [Fact]
    public void APositionIsRememberedAndOtherPacketsKeepIt()
    {
        var store = new StationStore(timeProvider: _time);
        store.Observe(P("N0CALL-9>APRS:!3858.80N/09440.20W>moving"));
        var heardAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMinutes(5));
        store.Observe(P("N0CALL-9>APRS:>a status"));

        var state = store.Find("n0call-9");
        Assert.NotNull(state);
        Assert.Equal("N0CALL-9", state.Callsign);
        Assert.Equal(_time.GetUtcNow(), state.LastHeard);
        Assert.Equal(heardAt, state.PositionTime);
        Assert.Equal(38.98, state.Position!.Value.Latitude, 2);
        Assert.Equal(-94.67, state.Position!.Value.Longitude, 2);
        Assert.Equal(state.Position, store.LastPosition("N0CALL-9"));
        Assert.Null(store.LastPosition("N0CALL"));
    }

    [Theory]
    [InlineData("N0CALL-9>S32UVT:`(_fn\"Oj/]Mic-E", true)]
    [InlineData("N0CALL-9>APRS:@092345z4903.50N/07201.75W_220/004g005t077", true)]
    [InlineData("N0CALL-9>APRS:;LEADER   *092345z4903.50N/07201.75W>an object", false)]
    [InlineData("N0CALL-9>APRS:)AID #2!4903.50N/07201.75WA", false)]
    [InlineData("N0CALL-9>APRS:!0000.00N\\00000.00W.unknown", false)]
    [InlineData("N0CALL-9>APRS:!9999.99N/09440.20W>broken", false)]
    public void OnlyAStationsOwnPositionIsItsPosition(string line, bool hasPosition)
    {
        var store = new StationStore(timeProvider: _time);
        store.Observe(P(line));
        Assert.NotNull(store.Find("N0CALL-9"));
        Assert.Equal(hasPosition, store.LastPosition("N0CALL-9") is not null);
    }

    [Fact]
    public void TheStationHeardLeastRecentlyIsForgottenFirst()
    {
        var store = new StationStore(capacity: 2, timeProvider: _time);
        Assert.Equal(2, store.Capacity);
        store.Observe(P("N1AAA>APRS:>a"));
        store.Observe(P("N1BBB>APRS:>b"));
        store.Observe(P("N1AAA>APRS:>a again"));
        store.Observe(P("N1CCC>APRS:>c"));
        Assert.Equal(2, store.Count);
        Assert.NotNull(store.Find("N1AAA"));
        Assert.Null(store.Find("N1BBB"));
        Assert.NotNull(store.Find("N1CCC"));
    }

    [Fact]
    public void PacketsWithoutASourceAreIgnored()
    {
        var store = new StationStore(timeProvider: _time);
        store.Observe(P("no header"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StationStore(0));
        Assert.Throws<ArgumentNullException>(() => new StationStore().Observe(null!));
        Assert.Throws<ArgumentNullException>(() => new StationStore().Find(null!));
    }
}
