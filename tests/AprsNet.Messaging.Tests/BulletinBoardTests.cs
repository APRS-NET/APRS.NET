using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Messaging.Tests;

public sealed class BulletinBoardTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly List<BulletinChangedEventArgs> _changes = [];
    private BulletinBoard _board;

    public BulletinBoardTests() => _board = Start(new BulletinBoardOptions());

    public void Dispose() => _board.Dispose();

    private BulletinBoard Start(BulletinBoardOptions options)
    {
        options.TimeProvider = _time;
        var board = new BulletinBoard(options);
        board.Changed += (_, e) => _changes.Add(e);
        return board;
    }

    private bool Hear(string line, string? source = "rf") => _board.Receive(AprsPacket.Parse(line), source);

    [Fact]
    public void ABulletinIsAdded()
    {
        Assert.True(Hear("WU2Z>APRS::BLN3     :Snow expected in Tampa RSN"));
        var bulletin = Assert.Single(_board.Bulletins);
        Assert.Equal("WU2Z", bulletin.From.Value);
        Assert.Equal(MessageKind.Bulletin, bulletin.Kind);
        Assert.Equal("BLN3", bulletin.Addressee);
        Assert.Equal("3", bulletin.Id);
        Assert.Null(bulletin.Group);
        Assert.Equal("Snow expected in Tampa RSN", bulletin.Text);
        Assert.Equal("rf", bulletin.Source);
        Assert.Equal(BulletinChange.Added, Assert.Single(_changes).Change);
    }

    [Fact]
    public void NewTextFromTheSameSenderReplacesIt()
    {
        Hear("WU2Z>APRS::BLN3     :Snow expected in Tampa RSN");
        _time.Advance(TimeSpan.FromMinutes(5));
        Hear("WU2Z>APRS::BLN3     :Snow cancelled");
        var bulletin = Assert.Single(_board.Bulletins);
        Assert.Equal("Snow cancelled", bulletin.Text);
        Assert.Equal(_time.GetUtcNow(), bulletin.FirstHeard);
        var change = _changes[^1];
        Assert.Equal(BulletinChange.Replaced, change.Change);
        Assert.Equal("Snow expected in Tampa RSN", change.Previous!.Text);
    }

    [Fact]
    public void ARepeatChangesNothingButWhenItWasHeard()
    {
        Hear("WU2Z>APRS::BLN3     :Snow expected in Tampa RSN", "rf");
        _time.Advance(TimeSpan.FromMinutes(10));
        Hear("WU2Z>APRS,TCPIP*,qAC,T2TEST::BLN3     :Snow expected in Tampa RSN", "aprs-is");
        var bulletin = Assert.Single(_board.Bulletins);
        Assert.Equal(_time.GetUtcNow(), bulletin.LastHeard);
        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromMinutes(10), bulletin.FirstHeard);
        Assert.Equal("aprs-is", bulletin.Source);
        Assert.Single(_changes);
    }

    [Fact]
    public void TheSameBulletinFromTwoSendersIsTwoBulletins()
    {
        Hear("WU2Z>APRS::BLN1     :From WU2Z");
        Hear("KB2ICI>APRS::BLN1     :From KB2ICI");
        Assert.Equal(["KB2ICI", "WU2Z"], _board.Bulletins.Select(b => b.From.Value));
    }

    [Fact]
    public void GroupBulletinsAndAnnouncementsAreKeptByGroup()
    {
        Hear("WU2Z>APRS::BLN4WX   :Storm watch");
        Hear("WU2Z>APRS::BLN4     :Not the WX group");
        Hear("WU2Z>APRS::BLNQ     :Hamfest Saturday");
        Hear("WU2Z>APRS::BLNALUX  :Club meeting");

        Assert.Equal(4, _board.Bulletins.Count);
        var group = _board.Bulletins.Single(b => b.Group == "WX");
        Assert.Equal((MessageKind.Bulletin, "4"), (group.Kind, group.Id));
        var announcement = _board.Bulletins.Single(b => b.Id == "Q");
        Assert.Equal(MessageKind.Announcement, announcement.Kind);
        Assert.Equal("LUX", _board.Bulletins.Single(b => b.Id == "A").Group);
        Assert.Equal(
            [MessageKind.Bulletin, MessageKind.Bulletin, MessageKind.Announcement, MessageKind.Announcement],
            _board.Bulletins.Select(b => b.Kind)
        );
    }

    [Fact]
    public void NwsBulletinsAreKeptApartByIdOrText()
    {
        Hear("NWS>APRS::NWS-WARN :Tornado warning Johnson County{AB1");
        Hear("NWS>APRS::NWS-WARN :Flood warning Wyandotte County{AB2");
        Hear("NWS>APRS::NWS_ADVIS:Wind advisory");
        Hear("NWS>APRS::NWS_ADVIS:Frost advisory");

        Assert.Equal(4, _board.Bulletins.Count);
        Assert.All(_board.Bulletins, b => Assert.Equal(MessageKind.NwsBulletin, b.Kind));
        Assert.Equal(["AB1", "AB2"], _board.Bulletins.Where(b => b.Id is not null).Select(b => b.Id));
    }

    [Fact]
    public void ABulletinNotHeardForTheExpiryIsRemoved()
    {
        Hear("WU2Z>APRS::BLN1     :First");
        _time.Advance(TimeSpan.FromHours(12));
        Hear("KB2ICI>APRS::BLN1     :Second");
        _time.Advance(TimeSpan.FromHours(12));

        var remaining = Assert.Single(_board.Bulletins);
        Assert.Equal("KB2ICI", remaining.From.Value);
        var removed = _changes[^1];
        Assert.Equal((BulletinChange.Removed, "WU2Z"), (removed.Change, removed.Bulletin.From.Value));

        _time.Advance(TimeSpan.FromHours(12));
        Assert.Empty(_board.Bulletins);
    }

    [Fact]
    public void BeingHeardAgainKeepsItLonger()
    {
        Hear("WU2Z>APRS::BLN1     :Still here");
        _time.Advance(TimeSpan.FromHours(20));
        Hear("WU2Z>APRS::BLN1     :Still here");
        _time.Advance(TimeSpan.FromHours(20));
        Assert.Single(_board.Bulletins);
    }

    [Fact]
    public void TheBoardStaysWithinItsLimit()
    {
        _board.Dispose();
        _board = Start(new BulletinBoardOptions { MaxBulletins = 2 });
        Hear("A1>APRS::BLN1     :One");
        _time.Advance(TimeSpan.FromMinutes(1));
        Hear("A2>APRS::BLN1     :Two");
        _time.Advance(TimeSpan.FromMinutes(1));
        Hear("A3>APRS::BLN1     :Three");

        Assert.Equal(["A2", "A3"], _board.Bulletins.Select(b => b.From.Value));
        Assert.Equal((BulletinChange.Removed, "A1"), (_changes[^1].Change, _changes[^1].Bulletin.From.Value));
    }

    [Fact]
    public void OtherPacketsAreIgnored()
    {
        Assert.False(Hear("WU2Z>APRS::N0CALL   :A message{1"));
        Assert.False(Hear("WU2Z>APRS:>A status"));
        Assert.Empty(_board.Bulletins);
        Assert.Empty(_changes);
    }

    [Fact]
    public void ABulletinGatedAsThirdPartyIsReadForThePacketItCarries()
    {
        Assert.True(Hear("IGATE>APRS,WIDE1-1:}WU2Z>APRS,TCPIP,IGATE*::BLN2     :Via the IGate"));
        Assert.Equal("WU2Z", Assert.Single(_board.Bulletins).From.Value);
    }
}
