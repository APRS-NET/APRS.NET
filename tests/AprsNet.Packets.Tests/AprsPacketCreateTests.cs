using System.Text;

namespace AprsNet.Packets.Tests;

public class AprsPacketCreateTests
{
    private static readonly Callsign _source = Callsign.Parse("N0CALL-9");
    private static readonly Callsign _destination = Callsign.Parse("APZ123");

    private static AprsPacket Create(AprsPayload payload, params string[] path) =>
        AprsPacket.Create(_source, _destination, path.Select(PathEntry.Parse), payload);

    private static string Information(AprsPayload payload) =>
        Encoding.Latin1.GetString(Create(payload).Information.Span);

    private static void Rejects(AprsPayload payload, string reason)
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(payload));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    // Header

    [Fact]
    public void WritesTheHeaderAndPath()
    {
        var packet = Create(new StatusReport { Text = "Net Control" }, "WIDE1-1", "WIDE2-2");

        Assert.Equal("N0CALL-9>APZ123,WIDE1-1,WIDE2-2:>Net Control", packet.ToString());
        Assert.Equal(ParseOutcome.Parsed, packet.Outcome);
        Assert.Equal([PathEntryKind.Alias, PathEntryKind.Alias], packet.Path!.Select(p => p.Kind));
    }

    [Fact]
    public void AnEmptyPathIsForAprsIs()
    {
        Assert.Equal("N0CALL-9>APZ123:>hi", Create(new StatusReport { Text = "hi" }).ToString());
    }

    [Fact]
    public void KeepsRepeatedMarkers()
    {
        var packet = Create(new StatusReport { Text = "hi" }, "N1DIGI*", "WIDE2-1");

        Assert.Equal("N0CALL-9>APZ123,N1DIGI*,WIDE2-1:>hi", packet.ToString());
        Assert.True(packet.Path![0].HasBeenRepeated);
    }

    [Fact]
    public void RejectsMoreThanTenPathEntries()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Create(new StatusReport(), [.. Enumerable.Repeat("WIDE1-1", 11)])
        );
        Assert.Equal("path", ex.ParamName);
    }

    [Fact]
    public void PayloadTypesWithoutAnEncoderAreNotSupportedYet()
    {
        var packet = AprsPacket.Parse("N0CALL>APRS:<IGATE,MSG_CNT=0");

        Assert.Throws<NotSupportedException>(() => Create(packet.Payload!));
    }

    // Callsigns and path entries

    [Theory]
    [InlineData("N0CALL")]
    [InlineData("N0CALL-15")]
    [InlineData("T2PERTH")]
    [InlineData("qAR")]
    [InlineData("EL-N0CALL")]
    public void CallsignsParse(string value)
    {
        Assert.Equal(value, Callsign.Parse(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("N0CALL-100")] // 10 characters
    [InlineData("N0 CALL")]
    [InlineData("N0CALL>")]
    [InlineData("N0CALL:")]
    [InlineData("N0,CALL")]
    [InlineData("N0CALL*")]
    [InlineData("NÖCALL")]
    public void CallsignsThatWouldBreakTheHeaderAreRejected(string value)
    {
        Assert.False(Callsign.TryParse(value, out var callsign));
        Assert.Null(callsign);
        Assert.Throws<ArgumentException>(() => Callsign.Parse(value));
    }

    [Theory]
    [InlineData("WIDE1-1", false, PathEntryKind.Alias)]
    [InlineData("WIDE2-1*", true, PathEntryKind.Alias)]
    [InlineData("N0CALL-3", false, PathEntryKind.Station)]
    [InlineData("TCPIP*", true, PathEntryKind.Alias)]
    [InlineData("qAR", false, PathEntryKind.QConstruct)]
    public void PathEntriesParse(string entry, bool repeated, PathEntryKind kind)
    {
        var parsed = PathEntry.Parse(entry);

        Assert.Equal(repeated, parsed.HasBeenRepeated);
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(entry, parsed.ToString());
    }

    // Status reports

    [Fact]
    public void EncodesAPlainStatus()
    {
        Assert.Equal(">Net Control Center", Information(new StatusReport { Text = "Net Control Center" }));
    }

    [Fact]
    public void EncodesAStatusWithATimestamp()
    {
        var status = new StatusReport { Timestamp = AprsTimestamp.DayHourMinute(9, 23, 45), Text = "Test" };

        Assert.Equal(">092345zTest", Information(status));
    }

    [Fact]
    public void EncodesAStatusWithALocatorAndBeamHeading()
    {
        var status = new StatusReport
        {
            MaidenheadLocator = "io91sx",
            Symbol = new AprsSymbol('/', 'G'),
            Text = " ^B7",
        };

        var packet = Create(status);

        Assert.Equal(">IO91SX/G ^B7", Encoding.Latin1.GetString(packet.Information.Span));
        var decoded = Assert.IsType<StatusReport>(packet.Payload);
        Assert.Equal(110, decoded.BeamHeading); // derived again by the parser
    }

    [Fact]
    public void EncodesStatusTextAsUtf8()
    {
        var packet = Create(new StatusReport { Text = "21°C 状态" });

        Assert.Equal([.. ">"u8, .. Encoding.UTF8.GetBytes("21°C 状态")], packet.Information.ToArray());
        Assert.Equal("21°C 状态", Assert.IsType<StatusReport>(packet.Payload).Text);
    }

    [Fact]
    public void ParsedTextKeepsItsOriginalBytes()
    {
        // Latin-1 text that is not valid UTF-8 must not be re-encoded.
        var parsed = AprsPacket.Parse([.. "N0CALL>APRS:>caf"u8, 0xE9]);

        var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

        Assert.Equal(parsed.Information.ToArray(), rebuilt.Information.ToArray());
    }

    [Fact]
    public void RejectsStatusErrors()
    {
        Rejects(
            new StatusReport
            {
                Timestamp = AprsTimestamp.DayHourMinute(1, 2, 3),
                MaidenheadLocator = "IO91",
                Symbol = new AprsSymbol('/', 'G'),
            },
            "not both"
        );
        Rejects(new StatusReport { Timestamp = AprsTimestamp.DayHourMinute(1, 2, 3, isLocal: true) }, "zulu");
        Rejects(new StatusReport { Timestamp = AprsTimestamp.HourMinuteSecond(1, 2, 3) }, "DDHHMMz");
        Rejects(new StatusReport { MaidenheadLocator = "IO91" }, "needs its symbol");
        Rejects(new StatusReport { MaidenheadLocator = "ZZ99", Symbol = new AprsSymbol('/', 'G') }, "not a 4- or 6");
        Rejects(
            new StatusReport
            {
                MaidenheadLocator = "IO91",
                Symbol = new AprsSymbol('/', 'G'),
                Text = "hi",
            },
            "start with a space"
        );
        Rejects(new StatusReport { Symbol = new AprsSymbol('/', 'G') }, "goes with a Maidenhead locator");
        Rejects(new StatusReport { Text = "line\nbreak" }, "control characters");
    }

    [Fact]
    public void RejectsStatusTextThatWouldBeMisread()
    {
        Rejects(new StatusReport { Text = "092345zTest" }, "read as a timestamp");
        Rejects(new StatusReport { Text = "IO91SX/G here" }, "read as a Maidenhead locator");
    }

    // Messages

    [Fact]
    public void EncodesAMessageWithAnId()
    {
        var message = new MessagePayload
        {
            Addressee = "WU2Z",
            Text = "Testing",
            MessageId = "003",
        };

        Assert.Equal(":WU2Z     :Testing{003", Information(message));
    }

    [Fact]
    public void EncodesAMessageWithoutAnId()
    {
        Assert.Equal(":WU2Z     :Hi", Information(new MessagePayload { Addressee = "WU2Z", Text = "Hi" }));
    }

    [Fact]
    public void EncodesReplyAcks()
    {
        Assert.Equal(
            ":WU2Z     :Hi{MM}AA",
            Information(
                new MessagePayload
                {
                    Addressee = "WU2Z",
                    Text = "Hi",
                    MessageId = "MM",
                    ReplyAck = "AA",
                }
            )
        );
        Assert.Equal(
            ":WU2Z     :Hi{MM}",
            Information(
                new MessagePayload
                {
                    Addressee = "WU2Z",
                    Text = "Hi",
                    MessageId = "MM",
                    ReplyAck = "",
                }
            )
        );
    }

    [Theory]
    [InlineData(MessageKind.Acknowledgement, ":KB2ICI-14:ack003")]
    [InlineData(MessageKind.Rejection, ":KB2ICI-14:rej003")]
    public void EncodesAcknowledgementsAndRejections(MessageKind kind, string expected)
    {
        Assert.Equal(
            expected,
            Information(
                new MessagePayload
                {
                    Addressee = "KB2ICI-14",
                    Kind = kind,
                    MessageId = "003",
                }
            )
        );
    }

    [Theory]
    [InlineData("BLN3", MessageKind.Bulletin, ":BLN3     :Snow on the roads")]
    [InlineData("BLN4WX", MessageKind.Bulletin, ":BLN4WX   :Snow on the roads")]
    [InlineData("BLNQ", MessageKind.Announcement, ":BLNQ     :Snow on the roads")]
    [InlineData("NWS-WARN", MessageKind.NwsBulletin, ":NWS-WARN :Snow on the roads")]
    public void EncodesBulletins(string addressee, MessageKind kind, string expected)
    {
        var message = new MessagePayload
        {
            Addressee = addressee,
            Kind = kind,
            Text = "Snow on the roads",
        };

        Assert.Equal(expected, Information(message));
    }

    [Fact]
    public void EncodesADirectedQuery()
    {
        var packet = Create(
            new MessagePayload
            {
                Addressee = "N1CALL",
                Kind = MessageKind.Query,
                Text = "?APRSD",
            }
        );

        var decoded = Assert.IsType<MessagePayload>(packet.Payload);
        Assert.Equal("APRSD", decoded.QueryType);
    }

    [Fact]
    public void AcceptsAddresseesThatServicesUse()
    {
        Assert.Equal(":NWS_WARN :x", Information(new MessagePayload { Addressee = "NWS_WARN", Text = "x" }));
        Assert.Equal(
            ":BLN3 SKYR:x",
            Information(
                new MessagePayload
                {
                    Addressee = "BLN3 SKYR",
                    Kind = MessageKind.Bulletin,
                    Text = "x",
                }
            )
        );
    }

    [Fact]
    public void RejectsMessageErrors()
    {
        Rejects(new MessagePayload { Addressee = "", Text = "x" }, "addressee");
        Rejects(new MessagePayload { Addressee = "WAYTOOLONG", Text = "x" }, "addressee");
        Rejects(new MessagePayload { Addressee = "WU2Z:", Text = "x" }, "addressee");
        Rejects(new MessagePayload { Addressee = "WU2Z", Text = "pipe | here" }, "'|', '~' or '{'");
        Rejects(new MessagePayload { Addressee = "WU2Z", Text = "brace { here" }, "'|', '~' or '{'");
        Rejects(new MessagePayload { Addressee = "WU2Z", Text = new string('x', 68) }, "67 characters");
        Rejects(
            new MessagePayload
            {
                Addressee = "WU2Z",
                Text = "x",
                MessageId = "123456",
            },
            "message ID"
        );
        Rejects(
            new MessagePayload
            {
                Addressee = "WU2Z",
                Text = "x",
                MessageId = "a-b",
            },
            "message ID"
        );
        Rejects(
            new MessagePayload
            {
                Addressee = "WU2Z",
                Text = "x",
                ReplyAck = "AA",
            },
            "needs a message ID"
        );
        Rejects(new MessagePayload { Addressee = "WU2Z", Kind = MessageKind.Acknowledgement }, "needs the message ID");
        Rejects(
            new MessagePayload
            {
                Addressee = "WU2Z",
                Kind = MessageKind.Rejection,
                Text = "x",
                MessageId = "1",
            },
            "carries no text"
        );
    }

    [Fact]
    public void RejectsAKindThatTheAddresseeOrTextContradicts()
    {
        Rejects(
            new MessagePayload
            {
                Addressee = "WU2Z",
                Kind = MessageKind.Bulletin,
                Text = "x",
            },
            "decodes as Message"
        );
        Rejects(new MessagePayload { Addressee = "BLN3", Text = "x" }, "decodes as Bulletin");
        Rejects(new MessagePayload { Addressee = "WU2Z", Text = "ack123" }, "decodes as Acknowledgement");
        Rejects(new MessagePayload { Addressee = "WU2Z", Text = "PARM.A,B" }, "TelemetryDefinition");
    }

    // Timestamps

    [Fact]
    public void TimestampFactoriesWriteTheWireForm()
    {
        Assert.Equal("092345z", AprsTimestamp.DayHourMinute(9, 23, 45).ToString());
        Assert.Equal("092345/", AprsTimestamp.DayHourMinute(9, 23, 45, isLocal: true).ToString());
        Assert.Equal("234517h", AprsTimestamp.HourMinuteSecond(23, 45, 17).ToString());
        Assert.Equal("02292345", AprsTimestamp.MonthDayHourMinute(2, 29, 23, 45).ToString());
    }

    [Fact]
    public void TimestampsComeFromUtcTimes()
    {
        var time = new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.FromHours(-5)); // 06:02:03 UTC

        Assert.Equal("140602z", AprsTimestamp.FromDateTimeOffset(time).ToString());
        Assert.Equal(
            "060203h",
            AprsTimestamp.FromDateTimeOffset(time, AprsTimestampFormat.HourMinuteSecond).ToString()
        );
        Assert.Equal(
            "10140602",
            AprsTimestamp.FromDateTimeOffset(time, AprsTimestampFormat.MonthDayHourMinute).ToString()
        );
    }

    [Fact]
    public void TimestampFactoriesCheckRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.DayHourMinute(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.DayHourMinute(32, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.DayHourMinute(1, 24, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.HourMinuteSecond(0, 60, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.HourMinuteSecond(0, 0, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.MonthDayHourMinute(13, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AprsTimestamp.MonthDayHourMinute(4, 31, 0, 0));
    }

    // Position reports

    private static readonly AprsSymbol _car = new('/', '>');

    // APRS101's example position: 49°03.50' N, 72°01.75' W.
    private static readonly AprsPosition _example = new(49 + (3.5 / 60), -(72 + (1.75 / 60)));

    private static PositionReport Decoded(AprsPayload payload) =>
        Assert.IsType<PositionReport>(Create(payload).Payload);

    [Fact]
    public void EncodesTheSpecExamplePosition()
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            Course = 88,
            SpeedKnots = 36,
            Comment = "Test",
        };

        Assert.Equal("!4903.50N/07201.75W>088/036Test", Information(report));
    }

    [Theory]
    [InlineData(false, null, "!")]
    [InlineData(true, null, "=")]
    [InlineData(false, "092345z", "/092345z")]
    [InlineData(true, "234517h", "@234517h")]
    public void ChoosesTheDataTypeFromMessagingAndTimestamp(bool messaging, string? timestamp, string prefix)
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            IsMessagingCapable = messaging,
            Timestamp = timestamp switch
            {
                "092345z" => AprsTimestamp.DayHourMinute(9, 23, 45),
                "234517h" => AprsTimestamp.HourMinuteSecond(23, 45, 17),
                _ => null,
            },
        };

        Assert.Equal(prefix + "4903.50N/07201.75W>", Information(report));
    }

    [Theory]
    [InlineData(-33.8675, 151.2083, "3352.05S/15112.50E>")]
    [InlineData(0, 0, "0000.00N/00000.00E>")]
    [InlineData(10.999999, -0.0000001, "1100.00N/00000.00W>")] // rounding carries into the degrees
    [InlineData(90, 180, "9000.00N/18000.00E>")]
    public void WritesHemispheresAndRounds(double latitude, double longitude, string expected)
    {
        var report = new PositionReport { Position = new AprsPosition(latitude, longitude), Symbol = _car };

        Assert.Equal("!" + expected, Information(report));
    }

    [Theory]
    [InlineData(1, "4903.5 N/07201.7 W>")]
    [InlineData(2, "4903.  N/07201.  W>")]
    [InlineData(3, "490 .  N/0720 .  W>")]
    [InlineData(4, "49  .  N/072  .  W>")]
    public void BlanksDigitsForAmbiguity(int ambiguity, string expected)
    {
        // The parser keeps blanked digits as zeros, so an ambiguous position is given as its box corner.
        var report = new PositionReport
        {
            Position = new AprsPosition(49 + (3.5 / 60), -(72 + (1.7 / 60)), ambiguity),
            Symbol = _car,
        };

        Assert.Equal("!" + expected, Information(report));
        Assert.Equal(ambiguity, Decoded(report).Position.Ambiguity);
    }

    [Fact]
    public void WritesOverlaySymbols()
    {
        var report = new PositionReport { Position = _example, Symbol = new AprsSymbol('S', '#') };

        Assert.Equal("!4903.50NS07201.75W#", Information(report));
    }

    [Theory]
    [InlineData(null, 36.0, ".../036")]
    [InlineData(88, null, "088/...")]
    [InlineData(360, 0.4, "360/000")]
    [InlineData(0, 999.4, "000/999")]
    public void WritesCourseAndSpeed(int? course, double? speed, string expected)
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            Course = course,
            SpeedKnots = speed,
        };

        Assert.Equal("!4903.50N/07201.75W>" + expected, Information(report));
    }

    [Fact]
    public void WritesPowerHeightGain()
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = new AprsSymbol('/', '#'),
            PowerHeightGain = new PowerHeightGain(25, 20, 3, 90),
        };

        Assert.Equal("!4903.50N/07201.75W#PHG5132", Information(report));
        Assert.Equal(new PowerHeightGain(25, 20, 3, 90), Decoded(report).PowerHeightGain);
    }

    [Fact]
    public void RoundsPhgToTheNearestCodes()
    {
        // 5 W is between the 4 W and 9 W codes; 100 ft is between 80 and 160.
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            PowerHeightGain = new PowerHeightGain(5, 100, 6, null),
        };

        Assert.Equal(new PowerHeightGain(4, 80, 6, null), Decoded(report).PowerHeightGain);
    }

    [Fact]
    public void WritesRangeAndDfSignalStrength()
    {
        Assert.EndsWith(
            "RNG0050",
            Information(
                new PositionReport
                {
                    Position = _example,
                    Symbol = _car,
                    RangeMiles = 50,
                }
            ),
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            "DFS2360",
            Information(
                new PositionReport
                {
                    Position = _example,
                    Symbol = new AprsSymbol('\\', '\\'),
                    DirectionFindingStrength = new DirectionFindingStrength(2, 80, 6, null),
                }
            ),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(1234.4, "/A=001234Hi")]
    [InlineData(-12, "/A=-00012Hi")]
    public void PutsTheAltitudeInFrontOfTheComment(double feet, string expected)
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            AltitudeFeet = feet,
            Comment = "Hi",
        };

        Assert.EndsWith(expected, Information(report), StringComparison.Ordinal);
        Assert.Equal(Math.Round(feet), Decoded(report).AltitudeFeet);
    }

    [Fact]
    public void DoesNotRepeatAnAltitudeTheCommentHas()
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            AltitudeFeet = 1234,
            Comment = "Hi /A=001234",
        };

        Assert.EndsWith(">Hi /A=001234", Information(report), StringComparison.Ordinal);
    }

    [Fact]
    public void EncodesACompressedPositionWithCourseAndSpeed()
    {
        var report = new PositionReport
        {
            Position = new AprsPosition(49.5, -72.75),
            Symbol = _car,
            IsCompressed = true,
            Course = 88,
            SpeedKnots = 36.2,
        };

        var decoded = Decoded(report);

        Assert.True(decoded.IsCompressed);
        Assert.Equal(49.5, decoded.Position.Latitude, 5);
        Assert.Equal(-72.75, decoded.Position.Longitude, 5);
        Assert.Equal(88, decoded.Course);
        Assert.Equal(36.2, decoded.SpeedKnots!.Value, 0);
    }

    [Fact]
    public void EncodesCompressedRangeAndAltitude()
    {
        var range = Decoded(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                IsCompressed = true,
                RangeMiles = 20,
            }
        );
        var altitude = Decoded(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                IsCompressed = true,
                AltitudeFeet = 10004,
                Comment = "Hi",
            }
        );

        Assert.Equal(20, range.RangeMiles!.Value, 0);
        Assert.InRange(altitude.AltitudeFeet!.Value, 10004 * 0.998, 10004 * 1.002); // 0.2% steps
        Assert.Equal("Hi", altitude.Comment); // carried in the compressed bytes, not the comment
    }

    [Fact]
    public void CompressedPositionsPutAnAltitudeWithCourseInTheComment()
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = _car,
            IsCompressed = true,
            Course = 90,
            SpeedKnots = 10,
            AltitudeFeet = 500,
        };

        Assert.EndsWith("/A=000500", Information(report), StringComparison.Ordinal);
    }

    [Fact]
    public void CompressedOverlayDigitsUseLowerCaseLetters()
    {
        var report = new PositionReport
        {
            Position = _example,
            Symbol = new AprsSymbol('3', '#'),
            IsCompressed = true,
        };

        Assert.StartsWith("!d", Information(report), StringComparison.Ordinal);
        Assert.Equal(new AprsSymbol('3', '#'), Decoded(report).Symbol);
    }

    [Fact]
    public void EncodesCommentTextAsUtf8()
    {
        Assert.Equal(
            "21°C",
            Decoded(
                new PositionReport
                {
                    Position = _example,
                    Symbol = _car,
                    Comment = "21°C",
                }
            ).Comment
        );
    }

    [Fact]
    public void ParsedPositionsRebuildWithTheirOriginalSpelling()
    {
        // Unknown course and speed as dots, and a compressed position's ignored csT bytes, are kept as sent.
        foreach (
            var line in new[] { "N0CALL>APRS:!4903.50N/07201.75W_.../...g005t077", "N0CALL>APRS:=/5L!!<*e7> sTComment" }
        )
        {
            var parsed = AprsPacket.Parse(line);

            var rebuilt = AprsPacket.Create(parsed.Source!, parsed.Destination!, parsed.Path!, parsed.Payload!);

            Assert.Equal(line, rebuilt.ToString());
        }
    }

    [Fact]
    public void RejectsPositionErrors()
    {
        Rejects(new PositionReport { Position = new AprsPosition(91, 0), Symbol = _car }, "Latitude");
        Rejects(new PositionReport { Position = new AprsPosition(0, -181), Symbol = _car }, "Longitude");
        Rejects(new PositionReport { Position = _example }, "needs a symbol");
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                Timestamp = AprsTimestamp.MonthDayHourMinute(1, 2, 3, 4),
            },
            "not MMDDHHMM"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                Course = 90,
                RangeMiles = 5,
            },
            "one data extension"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                Course = 361,
            },
            "Course"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                SpeedKnots = 1000,
            },
            "Speed"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                PowerHeightGain = new PowerHeightGain(4, 20, 3, 100),
            },
            "Directivity"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                PowerHeightGain = new PowerHeightGain(4, 5, 3, null),
            },
            "height"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                IsCompressed = true,
                PowerHeightGain = new PowerHeightGain(4, 20, 3, null),
            },
            "no PHG"
        );
        Rejects(
            new PositionReport
            {
                Position = new AprsPosition(49, -72, 2),
                Symbol = _car,
                IsCompressed = true,
            },
            "no ambiguity"
        );
        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = _car,
                Comment = "a\tb",
            },
            "control characters"
        );
    }

    [Fact]
    public void RejectsACommentThatWouldBeReadAsWeather()
    {
        var weatherStation = new AprsSymbol('/', '_');

        Rejects(
            new PositionReport
            {
                Position = _example,
                Symbol = weatherStation,
                Comment = "t075 nice day",
            },
            "weather"
        );
    }
}
