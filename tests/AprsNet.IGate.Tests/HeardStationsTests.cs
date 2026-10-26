using AprsNet.Ax25;
using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.IGate.Tests;

/// <summary>Which stations an IGate has heard on RF, and which of them count as local.</summary>
public sealed class HeardStationsTests
{
    private static readonly Callsign _station = Callsign.Parse("N0CALL-9");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private HeardStations Heard(int capacity = 10_000) =>
        new(new HeardStationsOptions { TimeProvider = _time, Capacity = capacity });

    private static AprsPacket Packet(string header) => AprsPacket.Parse(header + ":>test");

    [Fact]
    public void AStationHeardDirectIsLocal()
    {
        var heard = Heard();

        var station = heard.Record(Packet("N0CALL-9>APRS,WIDE1-1,WIDE2-1"));

        Assert.Equal(
            new HeardStation(_station, _time.GetUtcNow(), 0, 0) { FirstHeard = _time.GetUtcNow(), Packets = 1 },
            station
        );
        Assert.True(heard.IsLocal(_station));
        Assert.True(heard.IsLocal(Callsign.Parse("n0call-9")));
    }

    [Theory]
    [InlineData("N0CALL-9>APRS,DIGI1,WIDE1*,WIDE2-1", 1, true)]
    [InlineData("N0CALL-9>APRS,DIGI1,WIDE1,DIGI2*,WIDE2-1", 2, true)]
    [InlineData("N0CALL-9>APRS,DIGI1,DIGI2,DIGI3*", 3, false)]
    public void AStationHeardThroughDigipeatersIsLocalWithinTheHopLimit(string header, int hops, bool local)
    {
        var heard = Heard();

        Assert.Equal(hops, heard.Record(Packet(header))!.Hops);
        Assert.Equal(local, heard.IsLocal(_station));
    }

    [Fact]
    public void AStationHeardTooLongAgoIsNotLocal()
    {
        var heard = Heard();
        heard.Record(Packet("N0CALL-9>APRS"));

        _time.Advance(HeardStationsOptions.DefaultLocalWindow);
        Assert.True(heard.IsLocal(_station));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(heard.IsLocal(_station));
        Assert.NotNull(heard.Find(_station));
    }

    [Fact]
    public void AStationNeverHeardIsNotLocal()
    {
        var heard = Heard();
        heard.Record(Packet("N1ABC>APRS"));

        Assert.False(heard.IsLocal(_station));
        Assert.Null(heard.Find(_station));
    }

    [Fact]
    public void ACopyRepeatedByADigipeaterKeepsTheShorterPath()
    {
        var heard = Heard();
        heard.Record(Packet("N0CALL-9>APRS,WIDE1-1,WIDE2-1"));

        _time.Advance(TimeSpan.FromSeconds(3));
        heard.Record(Packet("N0CALL-9>APRS,DIGI1,WIDE1*,WIDE2-1"));
        heard.Record(Packet("N0CALL-9>APRS,DIGI1,WIDE1,DIGI2*,WIDE2*"));

        var station = heard.Find(_station)!;
        Assert.Equal(0, station.Hops);
        Assert.Equal(1, station.Packets);
        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromSeconds(3), station.LastHeard);
    }

    [Fact]
    public void ALaterPacketOverMoreHopsReplacesTheShorterPath()
    {
        var heard = Heard();
        heard.Record(Packet("N0CALL-9>APRS"));

        _time.Advance(TimeSpan.FromMinutes(10));
        heard.Record(Packet("N0CALL-9>APRS,DIGI1,DIGI2,DIGI3*"), port: 1);

        var station = heard.Find(_station)!;
        Assert.Equal(3, station.Hops);
        Assert.Equal(1, station.Port);
        Assert.Equal(2, station.Packets);
        Assert.Equal(_time.GetUtcNow(), station.LastHeard);
        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromMinutes(10), station.FirstHeard);
        Assert.False(heard.IsLocal(_station));
    }

    [Theory]
    // From APRS-IS: a q-construct, or TCPIP in the path.
    [InlineData("N0CALL-9>APRS,qAR,N1ABC")]
    [InlineData("N0CALL-9>APRS,TCPIP*")]
    [InlineData("N0CALL-9>APRS,WIDE1-1,qAC,T2TEST")]
    public void PacketsNotHeardOnRfAreIgnored(string header)
    {
        var heard = Heard();

        Assert.Null(heard.Record(Packet(header)));
        Assert.Equal(0, heard.Count);
    }

    [Fact]
    public void AnUnreadableHeaderIsIgnored()
    {
        var heard = Heard();

        Assert.Null(heard.Record(AprsPacket.Parse("not a packet")));
        Assert.Equal(0, heard.Count);
    }

    [Fact]
    public void ThirdPartyTrafficRecordsTheStationThatRelayedIt()
    {
        var heard = Heard();

        heard.Record(Packet("N0GW>APRS,WIDE2-1:}N1ABC-5>APRS,TCPIP,N0GW*"));

        Assert.NotNull(heard.Find(Callsign.Parse("N0GW")));
        Assert.Null(heard.Find(Callsign.Parse("N1ABC-5")));
    }

    [Fact]
    public void AFrameFromTheTncIsRecordedOnItsPort()
    {
        var heard = Heard();
        var frame = Ax25Frame.FromAprsPacket(Packet("N0CALL-9>APRS,DIGI1*,WIDE2-1"));

        var station = heard.Record(frame, 2);

        Assert.Equal(1, station!.Hops);
        Assert.Equal(2, station.Port);
    }

    [Fact]
    public void WhenFullTheStationHeardLongestAgoMakesRoom()
    {
        var heard = Heard(capacity: 3);
        foreach (var call in new[] { "N1AAA", "N1BBB", "N1CCC" })
        {
            heard.Record(Packet($"{call}>APRS"));
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        // Heard again, N1AAA is now the most recent; N1BBB is the oldest.
        heard.Record(Packet("N1AAA>APRS"));
        heard.Record(Packet("N1DDD>APRS"));

        Assert.Equal(3, heard.Count);
        Assert.Null(heard.Find(Callsign.Parse("N1BBB")));
        Assert.Equal(["N1DDD", "N1AAA", "N1CCC"], heard.Stations.Select(s => s.Station.Value));
    }

    [Fact]
    public void TheLimitsComeFromTheOptions()
    {
        var heard = new HeardStations(
            new HeardStationsOptions
            {
                TimeProvider = _time,
                LocalWindow = TimeSpan.FromMinutes(30),
                MaxLocalHops = 0,
            }
        );
        heard.Record(Packet("N0CALL-9>APRS,DIGI1*"));
        heard.Record(Packet("N1ABC>APRS"));
        _time.Advance(TimeSpan.FromMinutes(31));
        heard.Record(Packet("N2ABC>APRS"));

        Assert.False(heard.IsLocal(_station));
        Assert.False(heard.IsLocal(Callsign.Parse("N1ABC")));
        Assert.True(heard.IsLocal(Callsign.Parse("N2ABC")));
    }

    [Theory]
    [InlineData(0, 2, 10)]
    [InlineData(30, -1, 10)]
    [InlineData(30, 2, 0)]
    public void UnusableSettingsAreRefused(int minutes, int hops, int capacity)
    {
        var options = new HeardStationsOptions
        {
            LocalWindow = TimeSpan.FromMinutes(minutes),
            MaxLocalHops = hops,
            Capacity = capacity,
        };

        Assert.ThrowsAny<ArgumentException>(() => new HeardStations(options));
    }
}
