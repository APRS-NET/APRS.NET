using AprsNet.Packets;
using Microsoft.Extensions.Time.Testing;

namespace AprsNet.Messaging.Tests;

/// <summary>The queries and answers of APRS101 ch. 15, with KH2Z queried by N8UR as in its examples.</summary>
public sealed class QueryResponderTests
{
    private static readonly PacketAddress Kh2z = PacketAddress.Parse("KH2Z", "APZNET", []);
    private static readonly PositionReport Position = Payload<PositionReport>("/092345z4903.50N/07201.75W>");
    private static readonly StatusReport Status = Payload<StatusReport>(">092345zNet Control Center");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 23, 45, 0, TimeSpan.Zero));

    [Fact]
    public void APositionQueryIsAnsweredWithThePosition()
    {
        var answer = Answer("N8UR>APRS::KH2Z     :?APRSP", new QueryState { Position = Position, Status = Status });

        Assert.Equal("N8UR", answer.Querier.Value);
        Assert.Equal(QueryKind.Position, answer.Kind);
        Assert.False(answer.IsGeneral);
        Assert.Equal(TimeSpan.Zero, answer.Delay);
        Assert.Equal(["KH2Z>APZNET:/092345z4903.50N/07201.75W>"], Sent(answer));
    }

    [Fact]
    public void AStatusQueryIsAnsweredWithTheStatus()
    {
        var answer = Answer("N8UR>APRS::KH2Z     :?APRSS", new QueryState { Position = Position, Status = Status });

        Assert.Equal(["KH2Z>APZNET:>092345zNet Control Center"], Sent(answer));
    }

    [Fact]
    public void AStationsHeardDirectQueryListsThem()
    {
        var state = new QueryState { HeardDirect = [Callsign.Parse("WA1LOU"), Callsign.Parse("WD5IVD")] };

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSD", state);

        Assert.Equal(["KH2Z>APZNET::N8UR     :Directs= WA1LOU WD5IVD"], Sent(answer));
    }

    [Fact]
    public void ALongListOfStationsHeardDirectTakesSeveralMessages()
    {
        var heard = Enumerable.Range(0, 12).Select(i => Callsign.Parse($"N{i}CAL-{i}")).ToList();

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSD", new QueryState { HeardDirect = heard });

        var texts = answer.Payloads.Cast<MessagePayload>().Select(m => m.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.All(texts, text => Assert.StartsWith("Directs= ", text, StringComparison.Ordinal));
        Assert.All(texts, text => Assert.InRange(text.Length, 1, 67));
        Assert.Equal(heard.Select(c => c.Value), texts.SelectMany(t => t["Directs= ".Length..].Split(' ')));
        Assert.All(answer.Payloads, payload => Assert.Null(((MessagePayload)payload).MessageId));
    }

    [Fact]
    public void AHeardQueryGivesTheHourlyCountsAndThePositionAsAnObject()
    {
        Callsign? asked = null;
        var state = new QueryState
        {
            Heard = callsign =>
            {
                asked = callsign;
                return new HeardStation
                {
                    PacketsPerHour = [1, 3, 2, 0, 0, 4, 5, 6],
                    Position = new AprsPosition(49.058333, -72.029167),
                    Symbol = new AprsSymbol('/', '>'),
                    LastHeard = new DateTimeOffset(2026, 10, 9, 23, 45, 0, TimeSpan.Zero),
                };
            },
        };

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSH N0QBF    ", state);

        Assert.Equal("N0QBF", asked?.Value);
        Assert.Equal(
            [
                "KH2Z>APZNET:;N0QBF    *092345z4903.50N/07201.75W>",
                "KH2Z>APZNET::N8UR     :N0QBF HEARD: 1 3 2 . . 4 5 6",
            ],
            Sent(answer)
        );
    }

    [Fact]
    public void AHeardQueryForAStationNotHeardGoesUnanswered()
    {
        var state = new QueryState { Heard = _ => null };

        Assert.Null(Responder().Answer(Parse("N8UR>APRS::KH2Z     :?APRSH N0QBF"), state));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS::KH2Z     :?APRSH"), state));
    }

    [Fact]
    public void AHeardStationWithoutAPositionHasOnlyItsCounts()
    {
        var state = new QueryState { Heard = _ => new HeardStation { PacketsPerHour = [2] } };

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSH N0QBF", state);

        Assert.Equal(["KH2Z>APZNET::N8UR     :N0QBF HEARD: 2 . . . . . . ."], Sent(answer));
    }

    [Fact]
    public void AMessagesQuerySendsTheWaitingMessagesAgainWithTheirIds()
    {
        var state = new QueryState
        {
            MessagesWaiting = callsign =>
                callsign.Value == "N8UR"
                    ?
                    [
                        new MessagePayload
                        {
                            Addressee = "N8UR",
                            Text = "Testing",
                            MessageId = "003",
                        },
                    ]
                    : [],
        };

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSM", state);

        Assert.Equal(["KH2Z>APZNET::N8UR     :Testing{003"], Sent(answer));
    }

    [Fact]
    public void AnObjectsQueryIsAnsweredWithTheObjects()
    {
        var leader = Payload<ObjectReport>(";LEADER   *092345z4903.50N/07201.75W>");

        var answer = Answer("N8UR>APRS::KH2Z     :?APRSO", new QueryState { Objects = [leader] });

        Assert.Equal(["KH2Z>APZNET:;LEADER   *092345z4903.50N/07201.75W>"], Sent(answer));
    }

    [Theory]
    [InlineData("?APRST")]
    [InlineData("?PING?")]
    public void ATraceQueryIsAnsweredWithThePathItCameBy(string query)
    {
        var answer = Answer($"N8UR>APRS,DIGI1,WIDE*::KH2Z     :{query}", new QueryState());

        Assert.Equal(QueryKind.Trace, answer.Kind);
        Assert.Equal(["KH2Z>APZNET::N8UR     :N8UR>APRS,DIGI1,WIDE*:"], Sent(answer));
    }

    [Fact]
    public void ATraceTooLongForAMessageLeavesOffTheLastPathEntries()
    {
        var path = string.Join(',', Enumerable.Range(1, 8).Select(i => $"DIGI{i}-1*"));

        var answer = Answer($"N8UR>APRS,{path}::KH2Z     :?APRST", new QueryState());

        var text = Assert.IsType<MessagePayload>(Assert.Single(answer.Payloads)).Text;
        Assert.InRange(text.Length, 1, 67);
        Assert.StartsWith("N8UR>APRS,DIGI1-1*,DIGI2-1*", text, StringComparison.Ordinal);
        Assert.EndsWith("*:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("N8UR>APRS::N0CALL   :?APRSP")] // for another station
    [InlineData("N8UR>APRS::KH2Z     :?TELEM?")] // a query it doesn't know
    [InlineData("N8UR>APRS::KH2Z     :APRSP")] // not a query
    [InlineData("KH2Z>APRS:?APRS?")] // its own query, heard back
    [InlineData("N8UR>APRS:>092345zNet Control Center")]
    public void WhatIsNotAQueryForTheStationGoesUnanswered(string line)
    {
        Assert.Null(Responder().Answer(Parse(line), new QueryState { Position = Position, Status = Status }));
    }

    [Fact]
    public void AQueryTheStationHasNothingForGoesUnanswered()
    {
        Assert.Null(Responder().Answer(Parse("N8UR>APRS::KH2Z     :?APRSS"), new QueryState { Position = Position }));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS::KH2Z     :?APRSD"), new QueryState()));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS:?IGATE?"), new QueryState { Position = Position }));
    }

    [Fact]
    public void TheAddresseeMatchesWhateverItsCase()
    {
        Assert.NotNull(
            Responder().Answer(Parse("N8UR>APRS::kh2z     :?APRSP"), new QueryState { Position = Position })
        );
    }

    [Fact]
    public void AGeneralQueryIsAnsweredWithThePositionAndStatusAfterARandomWait()
    {
        var answer = Answer("N8UR>APRS:?APRS?", new QueryState { Position = Position, Status = Status }, 0.5);

        Assert.True(answer.IsGeneral);
        Assert.Equal(QueryKind.PositionAndStatus, answer.Kind);
        Assert.Equal(TimeSpan.FromSeconds(31), answer.Delay);
        Assert.Equal(
            ["KH2Z>APZNET:/092345z4903.50N/07201.75W>", "KH2Z>APZNET:>092345zNet Control Center"],
            Sent(answer)
        );
    }

    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(0.25, 16.5)]
    [InlineData(0.999999, 60)]
    public void TheWaitIsInsideTheWindow(double random, double seconds)
    {
        var answer = Answer("N8UR>APRS:?APRS?", new QueryState { Position = Position }, random);

        Assert.Equal(seconds, answer.Delay.TotalSeconds, 0.001);
    }

    [Fact]
    public void TheWaitsSpreadAcrossTheWindow()
    {
        var options = new QueryResponderOptions
        {
            TimeProvider = _time,
            Random = new Random(7),
            RepeatWindow = TimeSpan.Zero,
        };
        var responder = new QueryResponder(Kh2z.Source, options);

        var delays = Enumerable
            .Range(0, 200)
            .Select(_ => responder.Answer(Parse("N8UR>APRS:?APRS?"), new QueryState { Position = Position })!.Delay)
            .ToList();

        Assert.All(delays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60)));
        Assert.True(delays.Min() < TimeSpan.FromSeconds(10));
        Assert.True(delays.Max() > TimeSpan.FromSeconds(50));
    }

    [Fact]
    public void AnIGateQueryIsAnsweredWithTheCapabilities()
    {
        var state = new QueryState { Capabilities = CapabilitiesReport.ForIGate(43, 14) };

        var answer = Answer("N8UR>APRS:?IGATE?", state);

        Assert.Equal(QueryKind.IGate, answer.Kind);
        Assert.Equal(["KH2Z>APZNET:<IGATE,MSG_CNT=43,LOC_CNT=14"], Sent(answer));
    }

    [Fact]
    public void AWeatherQueryIsAnsweredWithTheWeatherAndThePosition()
    {
        var weather = Payload<WeatherReport>("_10090556c220s004g005t077r000p000P000h50b09900");

        var answer = Answer("N8UR>APRS:?WX?", new QueryState { Position = Position, Weather = weather });

        Assert.Equal(
            ["KH2Z>APZNET:_10090556c220s004g005t077r000p000P000h50b09900", "KH2Z>APZNET:/092345z4903.50N/07201.75W>"],
            Sent(answer)
        );
    }

    [Fact]
    public void AWeatherQueryIsAnsweredWithAPositionCarryingTheWeatherAlone()
    {
        var weather = Payload<PositionReport>("!4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900");

        var answer = Answer("N8UR>APRS:?WX?", new QueryState { Position = Position, Weather = weather });

        Assert.Equal(["KH2Z>APZNET:!4903.50N/07201.75W_220/004g005t077r000p000P000h50b09900"], Sent(answer));
    }

    [Fact]
    public void OnlyStationsInsideTheFootprintAnswer()
    {
        // APRS101's example position, 49.06 N 72.03 W, against areas around it and far from it.
        var state = new QueryState { Position = Position };

        Assert.NotNull(Responder().Answer(Parse("N8UR>APRS:?APRS? 49.5,-72.5,0050"), state));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS:?APRS? 34.02,-117.15,0200"), state));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS:?APRS? somewhere"), state));
        Assert.Null(Responder().Answer(Parse("N8UR>APRS:?APRS? 49.5,-72.5,0050"), new QueryState { Status = Status }));
    }

    [Fact]
    public void TheSameQueryIsAnsweredOnceWithinTheRepeatWindow()
    {
        var responder = Responder();
        var state = new QueryState { Position = Position };

        Assert.NotNull(responder.Answer(Parse("N8UR>APRS:?APRS?"), state));
        Assert.Null(responder.Answer(Parse("N8UR>APRS,WIDE1*:?APRS?"), state));
        Assert.NotNull(responder.Answer(Parse("N0QBF>APRS:?APRS?"), state));
        Assert.NotNull(responder.Answer(Parse("N8UR>APRS::KH2Z     :?APRSP"), state));

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.NotNull(responder.Answer(Parse("N8UR>APRS:?APRS?"), state));
    }

    [Fact]
    public void AQueryThroughAnIGateIsAnsweredToItsSender()
    {
        var line = "N0CALL-10>APRS,TCPIP*:}N8UR>APRS,TCPIP,N0CALL-10*::KH2Z     :?APRSP";

        var answer = Answer(line, new QueryState { Position = Position });

        Assert.Equal("N8UR", answer.Querier.Value);
    }

    [Fact]
    public void UnusableSettingsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryResponder(Kh2z.Source, new QueryResponderOptions { MinimumDelay = TimeSpan.FromSeconds(-1) })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryResponder(
                Kh2z.Source,
                new QueryResponderOptions
                {
                    MinimumDelay = TimeSpan.FromSeconds(10),
                    MaximumDelay = TimeSpan.FromSeconds(5),
                }
            )
        );
        Assert.Throws<ArgumentNullException>(() =>
            new QueryResponder(Kh2z.Source, new QueryResponderOptions { Random = null! })
        );
    }

    private QueryResponder Responder(double random = 0) =>
        new(Kh2z.Source, new QueryResponderOptions { TimeProvider = _time, Random = new FixedRandom(random) });

    private QueryAnswer Answer(string line, QueryState state, double random = 0)
    {
        var answer = Responder(random).Answer(Parse(line), state);
        Assert.NotNull(answer);
        return answer;
    }

    /// <summary>Each payload sent from KH2Z, as the line a receiver hears.</summary>
    private static string[] Sent(QueryAnswer answer) => [.. answer.Payloads.Select(p => Kh2z.Create(p).ToString())];

    private static AprsPacket Parse(string line)
    {
        var packet = AprsPacket.Parse(line);
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        return packet;
    }

    private static T Payload<T>(string information)
        where T : AprsPayload => Assert.IsType<T>(Parse("KH2Z>APRS:" + information).Payload);

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
