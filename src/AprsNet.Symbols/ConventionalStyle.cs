using System.Globalization;
using System.Text;

namespace AprsNet.Symbols;

/// <summary>
/// The conventional style: shapes and colours long-time APRS users recognise (green digipeater star, red car, blue
/// weather station), drawn new from the symbol descriptions.
/// </summary>
internal sealed class ConventionalStyle : ISymbolStyle
{
    private const string _red = "#d7322b";
    private const string _green = "#2f9e44";
    private const string _darkGreen = "#1b5e20";
    private const string _blue = "#1f63c6";
    private const string _navy = "#1d4687";
    private const string _sky = "#74c0fc";
    private const string _water = "#339af0";
    private const string _yellow = "#f4c430";
    private const string _orange = "#f08c00";
    private const string _flame = "#f76707";
    private const string _glass = "#bfe3ff";
    private const string _white = "#ffffff";
    private const string _charcoal = "#2b2b2b";
    private const string _steel = "#495057";
    private const string _grey = "#adb5bd";
    private const string _brown = "#8b5a3c";
    private const string _olive = "#5c7a3a";
    private const string _cloudWhite = "#f1f3f5";
    private const string _cloudGrey = "#ced4da";
    private const string _stormGrey = "#6c757d";

    // A cloud spanning y 11–33, shared by the weather symbols.
    private const string _cloudPath =
        "M13,33 C8,33 5,29 6,25 C7,21 11,20 13.5,21 C14.5,15 19,11 25,11.5 C30.5,12 34,15.5 34.5,19 C39.5,18 43,22 43,26.5 C43,30.5 40,33 36,33 Z";

    private const string _airlinerPath =
        "M24,3 C26.4,3 27,6 27,9 V18 L45,27 V31 L27,26 V37 L33,41.5 V45 L24,42.5 L15,45 V41.5 L21,37 V26 L3,31 V27 L21,18 V9 C21,6 21.6,3 24,3 Z";

    // Where an overlay character goes on each symbol: centre, height, and light (white) or dark glyph. Any alternate
    // symbol may carry an overlay (APRS 1.2), so symbols without a planned spot get a corner badge.
    private static readonly (double X, double Y, double H, bool Light) _cornerSpot = (38, 38, 10, false);

    private readonly Dictionary<string, (double X, double Y, double H, bool Light)> _overlaySpots = new()
    {
        ["\\#"] = (24, 26, 14, true),
        ["\\&"] = (24, 24, 16, true),
        ["\\0"] = (24, 24, 18, false),
        ["\\A"] = (24, 24, 18, false),
        ["\\>"] = (24, 27, 11, true),
        ["\\n"] = (24, 31, 14, true),
        ["\\W"] = (24, 24, 18, false),
        ["\\^"] = (24, 22, 10, true),
        ["\\_"] = (24, 26, 13, true),
        ["\\a"] = (24, 24, 16, true),
        ["\\c"] = (24, 27, 12, false),
        ["\\i"] = (24, 24, 18, true),
        ["\\s"] = (24, 29, 12, false),
        ["\\u"] = (16.5, 21, 14, false),
        ["\\v"] = (17, 24, 12, true),
        ["\\z"] = (24, 30, 15, false),
        ["\\D"] = (24, 35, 10, true),
        ["\\!"] = (24, 24, 22, true),
        ["\\$"] = (24, 24, 22, true),
    };

    private readonly Dictionary<string, Action<Svg>> _draw;

    // Labels a symbol shows only when no overlay character takes their place.
    private readonly Dictionary<string, Action<Svg>> _label;

    public ConventionalStyle()
    {
        _draw = new(StringComparer.Ordinal)
        {
            // Digipeaters, gateways and overlay shapes
            ["/#"] = s => s.Star(24, 25.5, 22, 9.5, _green).Circle(24, 25.5, 6, _white),
            ["\\#"] = s => s.Star(24, 25.5, 22, 9.5, _green),
            ["/&"] = s => Diamond(s, _charcoal).Text("HF", 24, 24, 10),
            ["\\&"] = s => Diamond(s, _charcoal),
            ["\\a"] = s => Diamond(s, "#c92a2a"),
            ["\\0"] = s => s.Backdrop(b => b.Circle(24, 24, 19, _yellow, width: 2)),
            ["\\A"] = s => s.Rect(6, 6, 36, 36, _white, rx: 3),
            ["\\i"] = s => s.Rect(6, 6, 36, 36, _orange, rx: 4),
            ["\\n"] = s => s.Polygon([(24, 4), (45, 42), (3, 42)], _red, width: 2),
            ["\\o"] = s => s.Circle(24, 24, 9, _white, Svg.Ink, 3),
            ["/n"] = s =>
                s.Circle(24, 24, 19, Svg.Ink, _white, 2)
                    .Circle(24, 24, 12.5, Svg.Cut(_white), null)
                    .Circle(24, 24, 6.5, Svg.Ink, null),
            ["/\\"] = s => s.Polygon([(24, 5), (44, 41), (4, 41)], _white, Svg.Ink, 3).Circle(24, 29, 3, Svg.Ink, null),
            ["\\c"] = s =>
                s.Backdrop(b => b.Circle(24, 24, 20, _blue, width: 2))
                    .Polygon([(24, 9), (38, 34), (10, 34)], _white, null),
            ["\\_"] = s => s.Star(24, 25.5, 22, 9.5, _green).Circle(24, 25.5, 6.5, _blue, _white, 1.4),

            // Markers
            ["//"] = s => s.Circle(24, 24, 8, _red, width: 2),
            ["/."] = s => s.Line("M11,11 L37,37 M37,11 L11,37", _red, 7, halo: 3),
            ["\\."] = s => s.Line(StrokeFont.Path("?", 24, 24, 36), _red, 6, halo: 3),
            ["/@"] = s =>
                s.Raw("<circle cx=\"24\" cy=\"24\" r=\"15\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"5\"/>")
                    .Raw(
                        "<circle cx=\"24\" cy=\"24\" r=\"15\" fill=\"none\" stroke=\"#f08c00\" stroke-width=\"3\" stroke-dasharray=\"5 4\"/>"
                    )
                    .Circle(24, 24, 6.5, _orange, width: 1.8),
            ["\\/"] = s =>
                s.Path("M24,45 C17,34 10,27 10,18 C10,10 16.5,4 24,4 C31.5,4 38,10 38,18 C38,27 31,34 24,45 Z", _red)
                    .Circle(24, 18, 5, Svg.Cut(_white)),
            ["/G"] = s => Grid(s, 3, _blue),
            ["/q"] = s => Grid(s, 4, _steel),
            ["\\l"] = s =>
                s.Raw(
                        "<rect x=\"4\" y=\"4\" width=\"26\" height=\"26\" fill=\"#4dabf7\" fill-opacity=\"0.3\" stroke=\"#1f63c6\" stroke-width=\"2.4\" stroke-dasharray=\"4 3\"/>"
                    )
                    .Raw(
                        "<circle cx=\"31\" cy=\"31\" r=\"13\" fill=\"#ff8787\" fill-opacity=\"0.3\" stroke=\"#d7322b\" stroke-width=\"2.4\" stroke-dasharray=\"4 3\"/>"
                    ),
            ["\\m"] = s =>
                s.Rect(22, 26, 4, 18, _stormGrey)
                    .Rect(5, 6, 38, 20, _yellow, rx: 3)
                    .Text("123", 24, 16, 10, false, cut: true),

            // Labels in badges and discs
            ["/%"] = s => Disc(s, _navy, "DX", 14),
            ["\\d"] = s => Disc(s, _yellow, "DX", 14, light: false),
            ["/,"] = s => Disc(s, "#556b2f", "BS", 14),
            ["\\,"] = s => Disc(s, _green, "GS", 14),
            ["\\!"] = s => s.Backdrop(b => b.Circle(24, 24, 20, _red, width: 2)),
            ["\\$"] = s => s.Backdrop(b => b.Rect(5, 5, 38, 38, _green, rx: 7)),
            ["/B"] = s => Badge(s, _navy, "BBS", 12),
            ["/I"] = s => Badge(s, "#1971c2", "IP", 16),
            ["/N"] = s => Badge(s, "#5f3dc4", "NTS", 12),
            ["/c"] = s => Badge(s, "#e8590c", "ICP", 12),
            ["/o"] = s => Badge(s, "#c2255c", "EOC", 12),
            ["\\M"] = s => Badge(s, _olive, "MARS", 9),
            ["\\P"] = s => Badge(s, _blue, "P", 26),
            ["\\?"] = s => Badge(s, _blue, "?", 24),
            ["\\X"] = s => Badge(s, _green, "RX", 16),
            ["/_"] = s => s.Backdrop(b => b.Circle(24, 24, 20, _blue, width: 2)).Text("WX", 24, 24, 13),
            ["/W"] = s => s.Backdrop(b => b.Circle(24, 24, 19.5, _white, _blue, 3.5)).Text("NWS", 24, 24, 11, false),
            ["\\W"] = s => s.Backdrop(b => b.Circle(24, 24, 19.5, _white, _blue, 3.5)),
            ["/h"] = s => s.Backdrop(b => b.Rect(5, 5, 38, 38, _blue, rx: 7)).Text("H", 24, 24, 24),

            // Computers
            ["/L"] = s => Monitor(s, "PC"),
            ["/M"] = s => Monitor(s, "M"),
            ["/Z"] = s => Monitor(s, "W"),
            ["/x"] = s => Monitor(s, "X"),
            ["/l"] = Laptop,
            ["/?"] = Server,
            ["/T"] = Sstv,

            // Radio
            ["/r"] = s => Repeater(s, _red),
            ["/m"] = s => Repeater(s, _blue),
            ["/y"] = Yagi,
            ["/`"] = Dish,
            ["/V"] = TvSet,
            ["\\8"] = Wifi,
            ["\\K"] = HandheldRadio,
            ["\\Y"] = Transceiver,
            ["\\\\"] = GpsReceiver,
            ["/$"] = s =>
                s.Path(
                    "M10,8 L17,6 L20,15 L16,18 Q19,26 28,31 L31,27 L40,30 L38,37 Q37,41 33,41 Q14,38 8,15 Q8,10 10,8 Z",
                    _charcoal,
                    _white,
                    1.8
                ),
            ["/]"] = s => s.Rect(5, 11, 38, 26, _white, rx: 2).Line("M6,12 L24,27 L42,12", Svg.Cut(Svg.Ink), 1.8),

            // Cars, vans and trucks (side views face right)
            ["/>"] = SideCar,
            ["\\>"] = TopCar,
            ["/v"] = s => Van(s, _blue),
            ["\\v"] = s => Van(s, _steel),
            ["/k"] = Pickup,
            ["\\k"] = Suv,
            ["/j"] = Jeep,
            ["/u"] = Semi,
            ["\\u"] = BoxTruck,
            ["/U"] = Bus,
            ["/R"] = Motorhome,
            ["/a"] = Ambulance,
            ["/f"] = FireTruck,
            ["/F"] = Tractor,
            ["\\j"] = Excavator,
            ["/<"] = Motorcycle,
            ["/b"] = Bicycle,
            ["/*"] = Snowmobile,
            ["/("] = MobileSatellite,
            ["/="] = Locomotive,
            ["\\="] = Locomotive,

            // Aircraft and space
            ["/^"] = s => s.Path(_airlinerPath, "#3f6fb5"),
            ["\\^"] = s => s.Path(_airlinerPath, _red),
            ["/'"] = SmallPlane,
            ["/g"] = Glider,
            ["/X"] = Helicopter,
            ["/O"] = Balloon,
            ["\\O"] = Rocket,
            ["/S"] = Shuttle,
            ["\\S"] = Satellite,

            // Water
            ["/s"] = PowerBoat,
            ["\\s"] = TopBoat,
            ["/Y"] = Sailboat,
            ["/C"] = Canoe,
            ["\\N"] = Buoy,
            ["\\L"] = Lighthouse,
            ["\\C"] = LifeRing,
            ["/i"] = Island,
            ["/w"] = s =>
                s.Path(
                        "M24,5 C30,15 37,21 37,30 C37,38 31,44 24,44 C17,44 11,38 11,30 C11,21 18,15 24,5 Z",
                        _water,
                        width: 1.8
                    )
                    .Line("M17,30 C17,26 19,23 21,21", _white, 2.2),

            // Buildings and places
            ["/-"] = House,
            ["\\-"] = s =>
            {
                House(s);
                s.Line("M24,7 V2 M10,4 L24,2 L38,4", Svg.Ink, 1.6, halo: 1.6, haloColor: _white);
            },
            ["/K"] = School,
            ["/d"] = FireStation,
            ["\\+"] = Church,
            ["\\h"] = Store,
            ["\\D"] = s =>
                s.Path("M5,42 V20 L24,9 L43,20 V42 Z", _grey).Path("M16,42 V31 Q24,22 32,31 V42 Z", Svg.Cut(_steel)),
            ["\\%"] = PowerPlant,
            ["\\z"] = s => s.Polygon([(24, 5), (44, 21), (44, 43), (4, 43), (4, 21)], _white, _blue, 3),
            ["/;"] = s =>
                s.Polygon([(24, 7), (45, 41), (3, 41)], _green)
                    .Polygon([(24, 20), (31, 41), (17, 41)], Svg.Cut(_darkGreen), null)
                    .Line("M2,41 H46", Svg.Ink, 2),
            ["\\;"] = s =>
                s.Rect(21.5, 28, 5, 15, _brown)
                    .Path(
                        "M24,5 C31,5 35,10 35,15 C39,17 40,23 37,27 C35,31 30,32 26,30 H22 C18,32 13,31 11,27 C8,23 9,17 13,15 C13,10 17,5 24,5 Z",
                        _green
                    ),
            ["/t"] = s =>
                s.Backdrop(b => b.Rect(5, 5, 38, 38, _blue, rx: 7))
                    .Path("M8,31 V17 H27 V31 Z M28,31 V22 H34 L39,27 V31 Z", _white, null)
                    .Circle(14, 32, 3.4, _blue, _white)
                    .Circle(34, 32, 3.4, _blue, _white),
            ["\\9"] = s => FuelPump(s, _blue),
            ["/H"] = HotelBed,
            ["\\R"] = Restaurant,
            ["\\r"] = Restrooms,

            // People, animals and services
            ["/["] = Person,
            ["/)"] = Wheelchair,
            ["/e"] = Horse,
            ["/p"] = Dog,
            ["/E"] = s =>
                s.Path("M3,24 Q24,4 45,24 Q24,44 3,24 Z", _white, width: 1.8)
                    .Circle(24, 24, 8.5, Svg.Cut(_blue))
                    .Circle(24, 24, 3.6, Svg.Ink, null)
                    .Circle(26.5, 21.5, 1.4, _white, null),
            ["/!"] = s => s.Star(24, 24, 21, 11, "#e0a800", 6).Circle(24, 24, 6, Svg.Cut(_blue)),
            ["/P"] = s =>
                s.Path("M24,3 L41,9 V22 C41,33 33,41 24,45 C15,41 7,33 7,22 V9 Z", _blue)
                    .Star(24, 22, 9, 4, Svg.Cut(_yellow)),
            ["/+"] = s => s.Path(Cross(24, 24, 32, 11), _red, width: 1.8),
            ["/A"] = s => s.Backdrop(b => b.Rect(5, 5, 38, 38, _green, rx: 7)).Path(Cross(24, 24, 26, 9), _white, null),
            ["/:"] = s =>
                s.Path(
                        "M24,3 C30,12 38,18 38,29 C38,38 32,45 24,45 C16,45 10,38 10,29 C10,22 15,18 17,12 C19,17 21,19 23,20 C23,14 22,9 24,3 Z",
                        _flame
                    )
                    .Path(
                        "M24,24 C28,28 31,31 31,36 C31,41 28,44 24,44 C20,44 17,41 17,36 C17,32 20,30 21,26 C22,28 23,29 24,30 Z",
                        _yellow,
                        null
                    ),
            ["\\)"] = s =>
                s.Backdrop(b => b.Rect(5, 5, 38, 38, "#e8590c", rx: 7))
                    .Open("translate(12 12) scale(0.5)")
                    .Path(
                        "M24,3 C30,12 38,18 38,29 C38,38 32,45 24,45 C16,45 10,38 10,29 C10,22 15,18 17,12 C19,17 21,19 23,20 C23,14 22,9 24,3 Z",
                        _white,
                        null
                    )
                    .Close()
                    .Circle(36, 12, 3, _yellow, null),
            ["\\'"] = s => s.Star(24, 24, 21, 10, _orange, 10).Star(24, 24, 12, 6, _yellow, 10),
            ["\\x"] = s =>
                s.Polygon([(24, 3), (45, 24), (24, 45), (3, 24)], _yellow, Svg.Ink, 2)
                    .Line("M17,17 L31,31 M31,17 L17,31", Svg.Cut(Svg.Ink), 4),
            ["\\y"] = s =>
                s.Backdrop(b => b.Circle(24, 24, 20, _navy, width: 2))
                    .Path("M7,24 Q24,9 41,24 Q24,39 7,24 Z", _white, null)
                    .Circle(24, 24, 6, Svg.Cut(_navy), null),
            ["\\Q"] = s =>
                s.Line(
                    "M3,26 H10 L14,16 L19,36 L24,8 L29,40 L33,20 L37,30 L39,26 H45",
                    _red,
                    2.8,
                    halo: 2.4,
                    haloColor: _white
                ),
            ["\\V"] = Vortac,

            // Weather
            ["\\U"] = s => Sun(s, 24, 24, 10, 20),
            ["\\("] = s => Cloud(s, _cloudWhite, 4),
            ["\\p"] = s =>
            {
                Sun(s, 17, 17, 7, 14);
                Cloud(s, _cloudWhite, 9);
            },
            ["\\`"] = s => Rain(Cloud(s, _cloudGrey, -3)),
            ["\\I"] = s =>
            {
                Sun(s, 33, 11, 5, 10);
                Rain(Cloud(s, _cloudGrey, -3));
            },
            ["\\*"] = s => Snowflake(s, 24, 24, 19),
            ["\\G"] = s => Snowflake(Snowflake(Cloud(s, _cloudGrey, -4), 16, 39, 5), 32, 39, 5),
            ["\\:"] = s =>
                Cloud(s, _cloudGrey, -3).Circle(15, 39, 3, _white).Circle(24, 43, 3, _white).Circle(33, 39, 3, _white),
            ["\\F"] = s => Snowflake(Rain(Cloud(s, _cloudGrey, -3), "#15aabf"), 39, 42, 4),
            ["\\e"] = s =>
                Cloud(s, _cloudGrey, -3)
                    .Line("M17,36 L15,42", _water, 2.6, halo: 1.6, haloColor: _white)
                    .Circle(26, 40, 2.6, _white)
                    .Circle(33, 37, 2.6, _white),
            ["\\J"] = s => Bolt(Cloud(s, _cloudGrey, -3)),
            ["\\T"] = s => Bolt(Rain(Cloud(s, _stormGrey, -3))),
            ["\\["] = s => Cloud(s, _stormGrey, -2).Path("M17,30 H31 V42 H17 Z", _stormGrey),
            ["\\f"] = s => Cloud(s, _stormGrey, -4).Path("M17,28 H31 L26.5,36 L24.5,46 L23,36 Z", _stormGrey),
            ["\\t"] = s =>
                s.Line(
                    "M8,9 H40 M11,16 H37 M15,23 H33 M19,30 H29 M22,37 H27 M24,43 H25.5",
                    _steel,
                    3.6,
                    halo: 2.4,
                    haloColor: _white
                ),
            ["\\{"] = s =>
                s.Line("M6,16 H42 M10,23 H38 M6,30 H42 M12,37 H36", "#868e96", 3.4, halo: 2.4, haloColor: _white),
            ["\\E"] = s =>
                s.Line(
                    "M14,43 C10,37 18,33 14,27 C10,21 18,17 14,11 M24,43 C20,37 28,33 24,27 C20,21 28,17 24,5 M34,43 C30,37 38,33 34,27 C30,21 38,17 34,11",
                    "#868e96",
                    3.4,
                    halo: 2.4,
                    haloColor: _white
                ),
            ["\\H"] = s =>
            {
                Sun(s, 24, 15, 7, 13);
                s.Line("M5,30 H43 M9,37 H39 M14,44 H34", "#868e96", 3.4, halo: 2.4, haloColor: _white);
            },
            ["\\B"] = s =>
                Snowflake(s, 16, 15, 9)
                    .Line(
                        "M4,31 H31 Q37,31 37,26 Q37,22 33,22 M4,39 H38 Q44,39 44,34",
                        "#4c6ef5",
                        3,
                        halo: 2.4,
                        haloColor: _white
                    ),
            ["\\b"] = s =>
                s.Line(
                        "M4,22 H31 Q37,22 37,17 Q37,13 33,13 M4,31 H38 Q44,31 44,36 M4,39 H24",
                        "#c08b4a",
                        3,
                        halo: 2.4,
                        haloColor: _white
                    )
                    .Circle(31, 41, 1.8, "#c08b4a", _white, 1)
                    .Circle(38, 44, 1.8, "#c08b4a", _white, 1)
                    .Circle(40, 8, 1.8, "#c08b4a", _white, 1),
            ["\\@"] = s =>
                s.Path("M31,21 C34,11 28,4 16,5 C22,8 25,12 24.5,16.5 Z", _red, width: 1.4)
                    .Path("M17,27 C14,37 20,44 32,43 C26,40 23,36 23.5,31.5 Z", _red, width: 1.4)
                    .Circle(24, 24, 9, _red, width: 1.6)
                    .Circle(24, 24, 3.2, Svg.Cut(_white), null),
            ["\\<"] = s => Pennant(s.Line("M12,4 V45", Svg.Ink, 2.6, halo: 2, haloColor: _white), 6),
            ["\\g"] = s => Pennant(Pennant(s.Line("M12,4 V45", Svg.Ink, 2.6, halo: 2, haloColor: _white), 6), 20),
            ["\\w"] = s =>
                s.Line(
                    "M3,22 Q8.5,17 14,22 T25,22 T36,22 T47,22 M3,31 Q8.5,26 14,31 T25,31 T36,31 T47,31 M3,40 Q8.5,35 14,40 T25,40 T36,40 T47,40",
                    _water,
                    3.4,
                    halo: 2.4,
                    haloColor: _white
                ),
        };

        foreach (var digit in "0123456789")
        {
            _draw[$"/{digit}"] = s =>
                s.Backdrop(b => b.Circle(24, 24, 19, _white, width: 2)).Text(digit.ToString(), 24, 24, 20, false);
        }

        _label = new(StringComparer.Ordinal)
        {
            ["\\W"] = s => s.Text("NWS", 24, 24, 11, false),
            ["\\c"] = s => s.Text("CD", 24, 27, 9, false),
            ["\\i"] = s => s.Circle(24, 24, 5, _white),
            ["\\!"] = s => s.Text("!", 24, 24, 24),
            ["\\$"] = s => s.Text("$", 24, 24, 24),
            ["\\D"] = s => s.Text("D", 24, 35, 10),
        };
    }

    /// <summary>The drawing and optional label for a symbol, for styles that restyle these shapes.</summary>
    public bool TryGetDrawing(string key, out Action<Svg> draw, out Action<Svg>? label)
    {
        label = _label.GetValueOrDefault(key);
        return _draw.TryGetValue(key, out draw!);
    }

    public string? Draw(string key, AprsSymbolCategory category, char? overlay)
    {
        if (!_draw.TryGetValue(key, out var draw))
        {
            return null;
        }

        var svg = new Svg();
        draw(svg);
        if (overlay is { } c && StrokeFont.Has(c))
        {
            if (!_overlaySpots.TryGetValue(key, out var spot))
            {
                spot = _cornerSpot;
                svg.Circle(spot.X, spot.Y, 9, _white, width: 1.6);
            }

            svg.Text(c.ToString(), spot.X, spot.Y, spot.H, spot.Light);
        }
        else if (_label.TryGetValue(key, out var label))
        {
            label(svg);
        }

        return svg.Build();
    }

    /// <summary>The international "not" sign, shown for symbols with no assigned meaning (APRS 1.1, A11-SYM-04).</summary>
    public string Unassigned() => new Svg().Circle(24, 24, 17, _white, _red, 5).Line("M12,12 L36,36", _red, 5).Build();

    private static Svg Diamond(Svg s, string fill) =>
        s.Polygon([(24, 3), (45, 24), (24, 45), (3, 24)], fill, _white, 2);

    private static void Badge(Svg s, string fill, string text, double height, bool light = true) =>
        s.Backdrop(b => b.Rect(5, 5, 38, 38, fill, rx: 7)).Text(text, 24, 24, height, light);

    private static void Disc(Svg s, string fill, string text, double height, bool light = true) =>
        s.Backdrop(b => b.Circle(24, 24, 19.5, fill, width: 2)).Text(text, 24, 24, height, light);

    private static string Cross(double cx, double cy, double length, double thickness)
    {
        var h = length / 2;
        var a = thickness / 2;
        return FormattableString.Invariant(
            $"M{cx - a},{cy - h} H{cx + a} V{cy - a} H{cx + h} V{cy + a} H{cx + a} V{cy + h} H{cx - a} V{cy + a} H{cx - h} V{cy - a} H{cx - a} Z"
        );
    }

    private static void Grid(Svg s, int cells, string color)
    {
        s.Backdrop(b => b.Rect(6, 6, 36, 36, _white));
        var step = 36.0 / cells;
        var d = new StringBuilder();
        for (var i = 1; i < cells; i++)
        {
            var at = Svg.N(6 + (i * step));
            d.Append(CultureInfo.InvariantCulture, $"M{at},7 V41 M7,{at} H41 ");
        }

        s.Line(d.ToString().Trim(), color, 1.8).Rect(6, 6, 36, 36, "none");
    }

    private static Svg Wheel(Svg s, double cx, double cy, double r = 5.2) =>
        s.Circle(cx, cy, r, Svg.Ink).Circle(cx, cy, r * 0.42, "#c9c9c9", null);

    private static void Monitor(Svg s, string label) =>
        s.Rect(20, 33, 8, 5, _grey)
            .Rect(13, 38, 22, 4, _grey, rx: 2)
            .Rect(5, 6, 38, 28, _charcoal, rx: 3)
            .Rect(8, 9, 32, 22, Svg.Cut(_blue), null)
            .Text(label, 24, 20, 11);

    private static void Laptop(Svg s) =>
        s.Rect(9, 8, 30, 22, _charcoal, rx: 2)
            .Rect(11.5, 10.5, 25, 17, _sky, null)
            .Path("M4,31 H44 L41,38 H7 Z", _grey);

    private static void Server(Svg s) =>
        s.Rect(13, 4, 22, 40, _grey, rx: 3)
            .Rect(16, 9, 16, 4, Svg.Cut(_steel), null)
            .Rect(16, 16, 16, 4, Svg.Cut(_steel), null)
            .Rect(16, 23, 16, 4, Svg.Cut(_steel), null)
            .Circle(31, 36, 1.9, "#40c057", null)
            .Circle(25.5, 36, 1.9, _yellow, null);

    private static void Sstv(Svg s) =>
        s.Rect(20, 33, 8, 5, _grey)
            .Rect(13, 38, 22, 4, _grey, rx: 2)
            .Rect(5, 6, 38, 28, _charcoal, rx: 3)
            .Rect(8, 9, 32, 22, _sky, null)
            .Polygon([(8, 31), (19, 17), (27, 26), (31, 22), (40, 31)], _green, null)
            .Circle(33, 15, 3, _yellow, null);

    private static void Repeater(Svg s, string waves) =>
        s.Line(
                "M24,12 L15,44 M24,12 L33,44 M20,23 H28 M18.5,31 H29.5 M17,38 H31 M20,23 L29.5,31 M28,23 L18.5,31",
                Svg.Ink,
                2.2,
                halo: 2.4,
                haloColor: _white
            )
            .Line("M17.5,4.5 A8,8 0 0,0 17.5,15.5 M13,1.5 A13,13 0 0,0 13,18.5", waves, 2.2, halo: 2, haloColor: _white)
            .Line("M30.5,4.5 A8,8 0 0,1 30.5,15.5 M35,1.5 A13,13 0 0,1 35,18.5", waves, 2.2, halo: 2, haloColor: _white)
            .Circle(24, 10, 3, waves);

    private static void Yagi(Svg s) =>
        s.Line("M24,16 V44 M5,16 H43 M8,6 V26 M16,7.5 V24.5 M24,9 V23 M32,10 V22 M40,11 V21", _white, 5)
            .Line("M24,16 V44 M5,16 H43", Svg.Ink, 2.6)
            .Line("M8,6 V26 M16,7.5 V24.5 M24,9 V23 M32,10 V22 M40,11 V21", Svg.Ink, 2.2);

    private static void Dish(Svg s) =>
        s.Path("M19,30 L14,44 H32 L27,30 Z", "#9aa3ad")
            .Path("M7,9 C4,25 17,38 38,35 C27,32 13,22 7,9 Z", "#e6e9ed", width: 1.8)
            .Path("M7,9 C9.5,22 21,33 38,35", "none", Svg.Ink, 1.8)
            .Line("M22.5,22 L34,11.5", Svg.Ink, 2, halo: 2, haloColor: _white)
            .Circle(35, 11, 3, _red);

    private static void TvSet(Svg s) =>
        s.Line("M24,14 L15,3 M24,14 L33,3", Svg.Ink, 2, halo: 2, haloColor: _white)
            .Rect(5, 14, 38, 28, _brown, rx: 4)
            .Rect(9, 18, 24, 20, _sky, rx: 3)
            .Circle(38, 24, 2, Svg.Ink, null)
            .Circle(38, 31, 2, Svg.Ink, null);

    // Three arcs a quarter-turn wide, at radii 9, 17 and 25 around the dot, so the gaps are even.
    private static void Wifi(Svg s) =>
        s.Line(
                "M17.64,33.64 A9,9 0 0,1 30.36,33.64 M11.98,27.98 A17,17 0 0,1 36.02,27.98 M6.32,22.32 A25,25 0 0,1 41.68,22.32",
                _blue,
                3.6,
                halo: 2.4,
                haloColor: _white
            )
            .Circle(24, 40, 3.6, _blue, _white, 1.4);

    private static void HandheldRadio(Svg s) =>
        s.Rect(18, 2, 4, 11, _charcoal, _white, 1.4)
            .Rect(15, 12, 18, 32, _charcoal, _white, 1.6)
            .Rect(18, 16, 12, 7, _glass, null)
            .Line("M19,28 H29 M19,31 H29 M19,34 H29", _grey, 1.4)
            .Text("K", 24, 39.5, 5);

    private static void Transceiver(Svg s) =>
        s.Line("M38,16 L42,5", Svg.Ink, 2, halo: 2, haloColor: _white)
            .Rect(4, 16, 40, 20, _charcoal, _white, 1.6)
            .Rect(8, 20, 16, 7, _sky, null)
            .Circle(34, 26, 5, _grey, null)
            .Rect(8, 30, 4, 3, _grey, null)
            .Rect(14, 30, 4, 3, _grey, null)
            .Rect(20, 30, 4, 3, _grey, null);

    private static void GpsReceiver(Svg s) =>
        s.Rect(12, 4, 24, 40, _steel, rx: 5)
            .Rect(15, 9, 18, 20, "#d3f9d8", null)
            .Path(
                "M24,25 C21,21 19,19 19,16.5 C19,13.7 21.2,12 24,12 C26.8,12 29,13.7 29,16.5 C29,19 27,21 24,25 Z",
                _red,
                null
            )
            .Circle(24, 16.5, 1.8, _white, null)
            .Circle(19, 36, 2, _grey, null)
            .Circle(29, 36, 2, _grey, null);

    private static void SideCar(Svg s) =>
        Wheel(
            Wheel(
                s.Path(
                        "M3,31 C3,27 5,25 9,24 L15,23 L20,16 C21,15 22,14.5 24,14.5 H33 C35,14.5 36,15 37,16 L41,22 C44,23 45,25 45,28 V32 C45,33 44,34 43,34 H5 C4,34 3,33 3,32 Z",
                        _red
                    )
                    .Path("M21.5,17 H26.5 V23 H17.5 Z", _glass, width: 1)
                    .Path("M28.5,17 H33 C34,17 34.5,17.3 35,18 L38.5,23 H28.5 Z", _glass, width: 1)
                    .Rect(42, 25.5, 2, 2.5, _yellow, null),
                13,
                34
            ),
            36,
            34
        );

    private static void TopCar(Svg s) =>
        s.Rect(11, 8, 3, 7, Svg.Ink, null)
            .Rect(34, 8, 3, 7, Svg.Ink, null)
            .Rect(11, 33, 3, 7, Svg.Ink, null)
            .Rect(34, 33, 3, 7, Svg.Ink, null)
            .Rect(13, 3, 22, 42, _red, rx: 8)
            .Path("M16.5,14 Q24,11 31.5,14 L30,19.5 Q24,18.5 18,19.5 Z", _glass, width: 1)
            .Path("M18,36 Q24,37 30,36 L31,40 Q24,42 17,40 Z", _glass, width: 1);

    private static void Van(Svg s, string color) =>
        Wheel(
            Wheel(
                s.Path("M3,34 V15 Q3,12 6,12 H30 Q33,12 35,15 L41,22 Q45,23 45,27 V34 Z", color)
                    .Path("M31,15 L37,22 H31 Z", _glass, width: 1)
                    .Rect(23, 15.5, 6, 6, _glass, null),
                12,
                34
            ),
            36,
            34
        );

    private static void Pickup(Svg s) =>
        Wheel(
            Wheel(
                s.Path("M3,33 V22 H22 V15 H34 L39,22 H43 Q45,22 45,24 V33 Z", _blue)
                    .Path("M24,17 H33 L36.5,22 H24 Z", _glass, width: 1),
                12,
                34
            ),
            36,
            34
        );

    private static void Suv(Svg s) =>
        Wheel(
            Wheel(
                s.Path("M3,33 V21 Q3,18 6,17 L12,12 H34 Q36,12 37,13 L41,19 Q45,20 45,24 V33 Z", _navy)
                    .Rect(13, 14, 9, 5, _glass, null)
                    .Rect(24, 14, 9, 5, _glass, null)
                    .Path("M35,14 L39,19 H35 Z", _glass, null),
                12,
                34
            ),
            36,
            34
        );

    private static void Jeep(Svg s) =>
        Wheel(
            Wheel(
                s.Circle(5, 26, 4.5, Svg.Ink)
                    .Path("M5,33 V22 H12 L15,16 H33 V22 H43 V33 Z", _olive)
                    .Path("M16.5,18 H31 V22 H14.8 Z", _glass, width: 1),
                13,
                34
            ),
            35,
            34
        );

    private static void Semi(Svg s) =>
        Wheel(
            Wheel(
                Wheel(
                    Wheel(
                        s.Rect(2, 12, 29, 19, _white)
                            .Path("M32,31 V17 H38 L43,23 Q45,24 45,26 V31 Z", _red)
                            .Path("M33.5,19 H37.5 L41,23.5 H33.5 Z", _glass, width: 1)
                            .Rect(2, 31, 43, 2.5, _charcoal, null),
                        8,
                        35,
                        3.8
                    ),
                    16,
                    35,
                    3.8
                ),
                34,
                35,
                3.8
            ),
            41,
            35,
            3.8
        );

    private static void BoxTruck(Svg s) =>
        Wheel(
            Wheel(
                Wheel(
                    s.Rect(2, 10, 29, 22, _white)
                        .Path("M32,32 V17 H38 L43,23 Q45,24 45,26 V32 Z", _red)
                        .Path("M33.5,19 H37.5 L41,23.5 H33.5 Z", _glass, width: 1)
                        .Rect(2, 32, 43, 2, _charcoal, null),
                    9,
                    35,
                    4
                ),
                24,
                35,
                4
            ),
            38,
            35,
            4
        );

    private static void Bus(Svg s) =>
        Wheel(
            Wheel(
                s.Rect(3, 12, 42, 22, "#f2b705", rx: 4)
                    .Rect(7, 16, 6, 7, _glass, null)
                    .Rect(15, 16, 6, 7, _glass, null)
                    .Rect(23, 16, 6, 7, _glass, null)
                    .Rect(31, 16, 5, 7, _glass, null)
                    .Rect(38.5, 16, 4, 10, _glass, null)
                    .Line("M4,28 H44", Svg.Ink, 1.2),
                12,
                34
            ),
            36,
            34
        );

    private static void Motorhome(Svg s) =>
        Wheel(
            Wheel(
                s.Path("M3,34 V12 Q3,10 5,10 H36 Q38,10 39,12 L44,22 Q45,24 45,26 V34 Z", _white)
                    .Rect(3.8, 25, 40.4, 3, "#e8590c", null)
                    .Rect(8, 14, 9, 6, _glass, null)
                    .Rect(20, 14, 8, 6, _glass, null)
                    .Path("M36,13 L40.5,22 H36 Z", _glass, width: 1)
                    .Rect(30, 15, 4, 9, "#dee2e6", null),
                12,
                34
            ),
            36,
            34
        );

    private static void Ambulance(Svg s) =>
        Wheel(
            Wheel(
                s.Path("M3,33 V14 Q3,12 5,12 H31 Q32.5,12 33.5,13.5 L38.5,20 L43,22 Q45,23 45,25 V33 Z", _white)
                    .Rect(3.8, 27, 40.4, 3, Svg.Cut(_red), null)
                    .Path("M32,15 L36.5,21 H32 Z", _glass, width: 1)
                    .Rect(15, 14.5, 4, 11, Svg.Cut(_red), null)
                    .Rect(11.5, 18, 11, 4, Svg.Cut(_red), null),
                12,
                33,
                5
            ),
            36,
            33,
            5
        );

    private static void FireTruck(Svg s) =>
        Wheel(
            Wheel(
                Wheel(
                    s.Path("M3,34 V18 H32 V14 Q32,12 34,12 H38 L44,22 V34 Z", _red)
                        .Path("M34,15 H37.5 L42,22 H34 Z", _glass, width: 1)
                        .Rect(3.8, 27, 40, 2.5, _white, null)
                        .Line(
                            "M5,13 H30 M5,16.5 H30 M8,13 V16.5 M13,13 V16.5 M18,13 V16.5 M23,13 V16.5 M28,13 V16.5",
                            _grey,
                            1.6,
                            halo: 1.4
                        ),
                    10,
                    34,
                    4.6
                ),
                22,
                34,
                4.6
            ),
            37,
            34,
            4.6
        );

    private static void Tractor(Svg s) =>
        s.Path("M7,31 V22 H20 V11 H33 V22 H40 V31 Z", _red)
            .Rect(22.5, 13.5, 8, 7, _glass, null)
            .Line("M10,22 V15", Svg.Ink, 2.2)
            .Circle(32, 33, 10.5, Svg.Ink)
            .Circle(32, 33, 4, "#c9c9c9", null)
            .Circle(11, 36, 6, Svg.Ink)
            .Circle(11, 36, 2.3, "#c9c9c9", null);

    private static void Excavator(Svg s) =>
        s.Line("M26,26 L36,11 L44,21", _yellow, 4.4, halo: 1.6)
            .Polygon([(40, 21), (46, 21), (44, 30), (38.5, 28)], _charcoal)
            .Rect(7, 23, 21, 12, _yellow)
            .Rect(9, 14, 10, 10, _yellow)
            .Rect(11, 16, 6, 5, _glass, null)
            .Rect(4, 35, 26, 8, _charcoal, rx: 4)
            .Circle(9, 39, 2, _grey, null)
            .Circle(25, 39, 2, _grey, null);

    private static void Motorcycle(Svg s) =>
        s.Line("M10,33 L21,30 M8,35 H22 M31,18 L38,33 M31,18 L29,12.5 H34.5", _steel, 3, halo: 1.8, haloColor: Svg.Ink)
            .Circle(10, 33, 8, _charcoal)
            .Circle(10, 33, 3.2, "#c9c9c9", null)
            .Circle(38, 33, 8, _charcoal)
            .Circle(38, 33, 3.2, "#c9c9c9", null)
            .Rect(17, 24, 12, 10, _steel, rx: 2)
            .Path(
                "M5,22 Q6,18.5 11,18.5 H16 Q18.5,15 25,15 H30 Q33.5,15 32.5,19 L30,24.5 H16 Q9,24.5 5,22 Z",
                _red,
                width: 1.6
            )
            .Path("M5,22 Q6,18.5 11,18.5 H16 L17,22 Q11,23.5 5,22 Z", _charcoal, null)
            .Circle(34.5, 20, 2.4, _yellow, width: 1.2);

    private static void Bicycle(Svg s) =>
        s.Ring(11, 32, 8, Svg.Ink, 2.6)
            .Ring(37, 32, 8, Svg.Ink, 2.6)
            .Line(
                "M11,32 L18,20 H32 L37,32 M18,20 L24,32 H11 M24,32 L32,20 M15,16 H21 M32,20 L30,14 H35",
                _red,
                2.4,
                halo: 2,
                haloColor: _white
            );

    private static void Snowmobile(Svg s) =>
        s.Rect(20, 30, 21, 7, _charcoal, rx: 3.5)
            .Path("M6,30 L12,21 Q14,19 17,19 H27 L31,14 H34 L33,22 L42,25 Q45,26 45,29 V31 H6 Z", _red)
            .Line("M12,31 V40 M4,40 H28 Q33,40 34,36", Svg.Ink, 2.4, halo: 1.8, haloColor: _white);

    private static void MobileSatellite(Svg s) =>
        Wheel(
            Wheel(
                s.Line("M18,15 L19.5,22 M18,14 L25,9", Svg.Ink, 2, halo: 1.6, haloColor: _white)
                    .Path("M10,20 C11,12 18,7 27,8 C24,13 18,18 10,20 Z", "#e6e9ed")
                    .Path("M4,38 V24 Q4,22 6,22 H34 L40,28 Q44,29 44,32 V38 Z", _white)
                    .Path("M35,24 L39,28.5 H35 Z", _glass, width: 1),
                12,
                38,
                4.5
            ),
            35,
            38,
            4.5
        );

    private static void Locomotive(Svg s) =>
        s.Polygon([(2, 40), (8, 31), (8, 40)], _charcoal, _white, 1.2)
            .Path("M11,19 V12 H9 V9 H18 V12 H16 V19 Z", _charcoal, _white, 1.2)
            .Rect(7, 18, 23, 14, _charcoal, _white, rx: 3)
            .Circle(22, 18, 3, _charcoal, _white, 1.2)
            .Rect(29, 10, 14, 23, _red)
            .Rect(32, 13, 7, 6, _glass, null)
            .Rect(5, 32, 39, 4, _charcoal, null)
            .Circle(14, 38, 5, _red)
            .Circle(25, 38, 5, _red)
            .Circle(37, 38, 4, _red);

    private static void SmallPlane(Svg s) =>
        s.Rect(3, 14, 42, 7, _red, rx: 2)
            .Path("M24,5 C26,5 26.8,8 26.8,11 V34 L32,38 V41 H16 V38 L21.2,34 V11 C21.2,8 22,5 24,5 Z", _red)
            .Line("M18.5,4 H29.5", Svg.Ink, 2, halo: 1.4, haloColor: _white);

    private static void Glider(Svg s) =>
        s.Rect(2, 16, 44, 4, _white, rx: 2)
            .Path("M24,6 C25.5,6 26,8 26,10 V40 H22 V10 C22,8 22.5,6 24,6 Z", _white)
            .Rect(16, 38, 16, 3.5, _white, rx: 1.5);

    private static void Helicopter(Svg s) =>
        s.Line(
                "M34,24 L44,20 M44,15 V25 M23,10 V15 M13,40 H34 M17,33 V40 M28,33 V40",
                Svg.Ink,
                2.4,
                halo: 1.6,
                haloColor: _white
            )
            .Path("M10,24 C10,18 15,15 21,15 H26 C32,15 36,20 36,26 C36,31 32,33 27,33 H18 C13,33 10,30 10,24 Z", _red)
            .Path("M12,24 C12,19.5 15,17.5 19,17.5 H21 V25 Z", _glass, width: 1)
            .Line("M5,10 H41", Svg.Ink, 2.4, halo: 1.6, haloColor: _white);

    private static void Balloon(Svg s) =>
        s.Line("M20.5,29 L22,39 M27.5,29 L26,39", Svg.Ink, 1.3)
            .Path("M24,2 C31.5,2 37,8 37,15.5 C37,23 29,28 26,30 H22 C19,28 11,23 11,15.5 C11,8 16.5,2 24,2 Z", _red)
            .Path("M17,10 C18,7 20.5,5.5 23,5.2", "none", _white, 2)
            .Rect(20, 39, 8, 6, _yellow);

    private static void Rocket(Svg s) =>
        s.Path("M20,34 H28 L24,45 Z", _flame, null)
            .Polygon([(18, 25), (10, 37), (18, 34)], _red)
            .Polygon([(30, 25), (38, 37), (30, 34)], _red)
            .Path("M24,3 C29,8 30,15 30,22 V34 H18 V22 C18,15 19,8 24,3 Z", _white)
            .Path("M24,3 C27,6 28.5,9 29.2,12 H18.8 C19.5,9 21,6 24,3 Z", _red, null)
            .Circle(24, 19, 3, _glass);

    private static void Shuttle(Svg s) =>
        s.Path(
                "M24,3 C27,6 28,10 28,16 V27 L41,37 V41 H29 L28,44 H20 L19,41 H7 V37 L20,27 V16 C20,10 21,6 24,3 Z",
                _white
            )
            .Path("M24,3 C26,5 27,7.5 27.4,10 H20.6 C21,7.5 22,5 24,3 Z", _charcoal, null);

    private static void Satellite(Svg s) =>
        s.Line("M24,18 V9 M19,8 Q24,12 29,8", Svg.Ink, 1.8, halo: 1.4, haloColor: _white)
            .Rect(4, 20, 14, 9, _blue)
            .Rect(30, 20, 14, 9, _blue)
            .Line("M8.7,20.5 V28.5 M13.3,20.5 V28.5 M34.7,20.5 V28.5 M39.3,20.5 V28.5", _sky, 1)
            .Rect(18, 17, 12, 15, _grey);

    private static void PowerBoat(Svg s) =>
        s.Line("M22,13 V6", Svg.Ink, 1.6)
            .Path("M16,19 V13 H27 V19 Z", _white)
            .Path("M11,27 V19 H31 L36,27 Z", _white)
            .Rect(14, 21, 5, 3.5, _glass, null)
            .Rect(21.5, 21, 5, 3.5, _glass, null)
            .Path("M2,27 H46 L40.5,38 Q39.5,39.5 37.5,39.5 H10.5 Q8.5,39.5 7.5,38 Z", _navy);

    private static void TopBoat(Svg s) =>
        s.Path("M24,3 C31,10 34,17 34,26 V40 Q34,44 30,44 H18 Q14,44 14,40 V26 C14,17 17,10 24,3 Z", _white, width: 1.8)
            .Rect(18, 22, 12, 13, _glass, rx: 2);

    private static void Sailboat(Svg s) =>
        s.Line("M24,5 V35", Svg.Ink, 2)
            .Path("M26,7 L26,32 L41,32 Z", _white)
            .Path("M22,10 L22,32 L9,32 Z", _white)
            .Path("M5,35 H43 L38,42 H10 Z", _navy);

    private static void Canoe(Svg s) =>
        s.Line("M34,8 L18,36", _brown, 2.4, halo: 1.6, haloColor: _white)
            .Path("M16.5,35 L19,39 L15,40 Z", _brown)
            .Path("M3,25 Q24,41 45,25 Q24,31 3,25 Z", "#c96f2d", width: 1.8);

    // A pillar buoy: tapered body with a white band, a light on a short mast, floating on a round base.
    private static void Buoy(Svg s) =>
        s.Line("M24,16 V8", Svg.Ink, 1.8, halo: 1.6, haloColor: _white)
            .Circle(24, 6.5, 2.8, _yellow)
            .Path("M12,35 H36 Q36,41 24,41 Q12,41 12,35 Z", _charcoal, _white, 1.4)
            .Path("M18,35 L20,16 H28 L30,35 Z", _red)
            .Path("M19.15,24 H28.85 L29.25,28 H18.75 Z", Svg.Cut(_white), null)
            .Line("M3,40 Q8,36 13,40 T23,40 T33,40 T43,40", _water, 2.4, halo: 1.6, haloColor: _white);

    private static void Lighthouse(Svg s) =>
        s.Raw("<polygon points=\"28,10 46,4 46,16\" fill=\"#f4c430\" fill-opacity=\"0.7\"/>")
            .Raw("<polygon points=\"20,10 2,4 2,16\" fill=\"#f4c430\" fill-opacity=\"0.7\"/>")
            .Path("M19,42 L21,15 H27 L29,42 Z", _white)
            .Path("M20.6,23 H27.4 L27.9,29 H20.1 Z", Svg.Cut(_red), null)
            .Path("M19.7,35 H28.3 L28.8,41 H19.2 Z", Svg.Cut(_red), null)
            .Rect(20, 8, 8, 7, _yellow)
            .Polygon([(19, 8), (24, 3), (29, 8)], _red)
            .Rect(14, 42, 20, 3, _charcoal, null);

    private static void LifeRing(Svg s) =>
        s.Raw("<circle cx=\"24\" cy=\"24\" r=\"16\" fill=\"none\" stroke=\"#1b1b1b\" stroke-width=\"11.6\"/>")
            .Raw("<circle cx=\"24\" cy=\"24\" r=\"16\" fill=\"none\" stroke=\"#f76707\" stroke-width=\"9\"/>")
            .Raw(
                "<circle cx=\"24\" cy=\"24\" r=\"16\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"9\" stroke-dasharray=\"8 17.13\" stroke-dashoffset=\"4\"/>"
            );

    private static void Island(Svg s) =>
        s.Path("M2,38 Q24,28 46,38 V44 H2 Z", _water, null)
            .Path("M8,38 Q24,29 40,38 Z", "#f2d49b")
            .Line("M24,34 Q23,24 27,14", _brown, 2.6, halo: 1.4, haloColor: _white)
            .Path("M27,14 Q20,8 12,12 Q20,11 27,14 Z", _green, width: 1)
            .Path("M27,14 Q34,7 41,12 Q34,10 27,14 Z", _green, width: 1)
            .Path("M27,14 Q24,6 28,3 Q27,8 27,14 Z", _green, width: 1)
            .Path("M27,14 Q31,16 34,22 Q30,17 27,14 Z", _green, width: 1)
            .Path("M27,14 Q20,16 17,22 Q23,16 27,14 Z", _green, width: 1);

    private static void House(Svg s) =>
        s.Rect(31, 9, 5, 10, "#8b5a3c")
            .Rect(9, 22, 30, 20, "#f2e3c0")
            .Polygon([(4, 24), (24, 6), (44, 24)], "#c23b2b", width: 1.8)
            .Rect(21, 30, 7, 12, "#7a4a21")
            .Rect(12.5, 27, 6, 6, _glass)
            .Rect(30.5, 27, 6, 6, _glass);

    private static void School(Svg s) =>
        s.Rect(20, 5, 8, 10, _white)
            .Polygon([(19, 6), (24, 1.5), (29, 6)], _stormGrey, width: 1.2)
            .Rect(7, 22, 34, 20, "#c23b2b")
            .Polygon([(4, 23), (24, 11), (44, 23)], _stormGrey)
            .Rect(21, 31, 6, 11, Svg.Cut(_white))
            .Rect(11, 27, 6, 6, _glass)
            .Rect(31, 27, 6, 6, _glass);

    private static void FireStation(Svg s) =>
        s.Rect(5, 14, 38, 28, _red)
            .Rect(3, 10, 42, 5, "#a61e1e")
            .Rect(9, 22, 13, 20, Svg.Cut(_white))
            .Rect(26, 22, 13, 20, Svg.Cut(_white))
            .Line("M9,27 H22 M9,32 H22 M9,37 H22 M26,27 H39 M26,32 H39 M26,37 H39", _grey, 1.2);

    private static void Church(Svg s) =>
        s.Rect(10, 23, 28, 19, _white)
            .Polygon([(8, 24), (24, 14), (40, 24)], _grey)
            .Rect(20, 12, 8, 12, _white)
            .Polygon([(19, 13), (24, 7), (29, 13)], _grey)
            .Line("M24,1.5 V7 M21.5,3.5 H26.5", Svg.Ink, 1.8, halo: 1.4, haloColor: _white)
            .Path("M20,42 V35 Q24,30 28,35 V42 Z", "#7a4a21");

    private static void Store(Svg s) =>
        s.Rect(6, 18, 36, 24, _white)
            .Path("M4,18 L8,9 H40 L44,18 Z", _red)
            .Line("M14,9.5 L12,17.5 M22,9.5 L21.3,17.5 M26,9.5 L26.7,17.5 M34,9.5 L36,17.5", _white, 2.4)
            .Rect(20, 28, 8, 14, _glass)
            .Rect(10, 24, 7, 8, _glass)
            .Rect(31, 24, 7, 8, _glass);

    private static void PowerPlant(Svg s) =>
        s.Rect(35, 6, 6, 12, _steel)
            .Path("M5,42 V24 L15,30 V24 L25,30 V24 L35,30 V16 H42 V42 Z", "#868e96")
            .Path("M23,31 L18,38 H22 L20,44 L27,36 H23 L25,31 Z", _yellow, width: 1.2);

    private static void FuelPump(Svg s, string color) =>
        s.Line("M30,16 H34 Q37,16 37,19 V32 Q37,35 34,35", Svg.Ink, 2.4, halo: 1.6, haloColor: _white)
            .Rect(10, 7, 20, 34, color, rx: 3)
            .Rect(14, 11, 12, 8, Svg.Cut(_white), null)
            .Rect(7, 40, 26, 4, _charcoal, null);

    private static void HotelBed(Svg s) =>
        s.Backdrop(b => b.Rect(5, 5, 38, 38, _blue, rx: 7))
            .Line("M11,15 V35 M11,29 H37 V35", _white, 3.2)
            .Rect(14, 22, 7, 5, _white, null, rx: 2)
            .Path("M23,22 H35 Q37,22 37,25 V29 H23 Z", _white, null);

    private static void Restaurant(Svg s) =>
        s.Backdrop(b => b.Rect(5, 5, 38, 38, "#e8590c", rx: 7))
            .Line("M17,10 V38 M13.5,10 V18 Q13.5,22 17,22 Q20.5,22 20.5,18 V10", _white, 2.4)
            .Path("M31,10 Q37,15 35,25 H31 Z", _white, null)
            .Line("M31,10 V38", _white, 2.4);

    private static void Restrooms(Svg s) =>
        s.Backdrop(b => b.Rect(5, 5, 38, 38, _blue, rx: 7))
            .Circle(15, 12, 3, _white, null)
            .Rect(12, 17, 6, 12, _white, null, rx: 2)
            .Line("M13.5,29 V38 M16.5,29 V38", _white, 2.4)
            .Circle(33, 12, 3, _white, null)
            .Polygon([(33, 16.5), (38, 31), (28, 31)], _white, null)
            .Line("M31.5,31 V38 M34.5,31 V38", _white, 2.4)
            .Line("M24,9 V39", _white, 1.4);

    private static void Person(Svg s) =>
        s.Line(
                "M24,15 V29 M14,22.5 L24,18 L34,22.5 M24,29 L17,43 M24,29 L31,43",
                "#2b4c9b",
                5,
                halo: 2.5,
                haloColor: _white
            )
            .Circle(24, 8.5, 5.2, "#2b4c9b");

    private static void Wheelchair(Svg s) =>
        s.Backdrop(b => b.Rect(5, 5, 38, 38, _blue, rx: 7))
            .Circle(21, 11, 3.3, _white, null)
            .Line("M21,16 V27 H31 L35,35 M21,21 H28", _white, 3.2)
            .Line("M15.5,23.5 A9.5,9.5 0 1,0 30.5,33", _white, 2.8);

    private static void Horse(Svg s) =>
        s.Line("M11.5,20 Q5.5,22 6.5,33", "#5c3a21", 3, halo: 1.8, haloColor: _white)
            .Rect(17.5, 27, 3, 15, "#6d4429", rx: 1)
            .Rect(32, 27, 3, 15, "#6d4429", rx: 1)
            .Rect(13, 27, 3, 15, _brown, rx: 1)
            .Rect(28, 27, 3, 15, _brown, rx: 1)
            .Polygon([(37.5, 7), (39, 2.5), (40.5, 7.5)], _brown, width: 1.2)
            .Path(
                "M11,22 C11,18 14,16.5 19,16.5 H29 L34.5,9 C35.5,7 37.5,6 39.5,6.5 L41.5,7.5 L45.5,13.5 C46,15.2 44.8,16.5 43,15.8 L39.5,14.5 L36,22 C36,27 33.5,30.5 29,30.5 H17 C13,30.5 11,27 11,22 Z",
                _brown
            )
            .Path("M29.5,16.5 L35,8.6 L37.4,8.4 L32.6,17.2 Z", "#5c3a21", null)
            .Circle(40, 10, 1.1, Svg.Ink, null);

    // Lower and longer than the horse, with short legs and a raised tail, so the two read apart at map sizes.
    private static void Dog(Svg s) =>
        s.Open("translate(0 6)")
            .Line("M10,21 Q5,17 6.5,11", "#b07a3b", 3, halo: 1.8, haloColor: Svg.Ink)
            .Rect(16.5, 25, 3, 10, "#8a5d2b", rx: 1)
            .Rect(33, 25, 3, 10, "#8a5d2b", rx: 1)
            .Rect(12, 25, 3, 10, "#b07a3b", rx: 1)
            .Rect(29, 25, 3, 10, "#b07a3b", rx: 1)
            .Path(
                "M9,24 C9,20.5 11.5,19 15,19 H29 C30,19 31,18.5 31.5,17.5 L33,13 C33.5,11 35,10 37,10 L38.5,6 L40.5,10 L43,11.5 C45.5,12.5 46,15 44,16 L40,16.5 L38,22 C37,26 35,28 32,28.5 H15 C11.5,28.5 9,27 9,24 Z",
                "#b07a3b"
            )
            .Circle(39.5, 12.6, 1.1, Svg.Ink, null)
            .Circle(44.6, 14.4, 1.2, Svg.Ink, null)
            .Close();

    private static void Vortac(Svg s)
    {
        var corners = Enumerable
            .Range(0, 6)
            .Select(i => (X: 24 + (13 * Math.Cos(i * Math.PI / 3)), Y: 24 + (13 * Math.Sin(i * Math.PI / 3))))
            .ToList();
        s.Polygon(corners, Svg.Cut(_white), null)
            .Line("M" + string.Join(" L", corners.Select(c => $"{Svg.N(c.X)},{Svg.N(c.Y)}")) + " Z", _blue, 2.6);
        foreach (var angle in new[] { 30, 150, 270 })
        {
            s.Open(FormattableString.Invariant($"rotate({angle} 24 24)")).Rect(37.5, 18, 6, 12, _blue, _white).Close();
        }

        s.Circle(24, 24, 3, _blue, null);
    }

    private static void Sun(Svg s, double cx, double cy, double r, double rays)
    {
        var d = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            d.Append(
                CultureInfo.InvariantCulture,
                $"M{Svg.N(cx + ((r + 3) * Math.Cos(a)))},{Svg.N(cy + ((r + 3) * Math.Sin(a)))} L{Svg.N(cx + (rays * Math.Cos(a)))},{Svg.N(cy + (rays * Math.Sin(a)))} "
            );
        }

        s.Line(d.ToString().Trim(), "#f59f00", 2.8, halo: 1.6, haloColor: _white).Circle(cx, cy, r, _yellow, "#e67700");
    }

    private static Svg Cloud(Svg s, string fill, double dy) =>
        s.Open(FormattableString.Invariant($"translate(0 {dy})")).Path(_cloudPath, fill, width: 1.8).Close();

    private static Svg Rain(Svg s, string color = _water) =>
        s.Line("M15,36 L13,42 M24,36 L22,42 M33,36 L31,42", color, 2.6, halo: 1.6, haloColor: _white);

    private static void Bolt(Svg s) => s.Path("M27,24 L18,36 H23.5 L20,46 L31,32 H25.5 L29,24 Z", _yellow, width: 1.4);

    private static Svg Snowflake(Svg s, double cx, double cy, double r)
    {
        var x = r * 0.87;
        var y = r / 2;
        var d = FormattableString.Invariant(
            $"M{cx},{cy - r} V{cy + r} M{cx - x},{cy - y} L{cx + x},{cy + y} M{cx - x},{cy + y} L{cx + x},{cy - y}"
        );
        return s.Line(d, "#1c7ed6", Math.Max(1.8, r / 6), halo: 1.8, haloColor: _white);
    }

    private static Svg Pennant(Svg s, double top) =>
        s.Polygon([(13, top), (40, top + 6.5), (13, top + 13)], _red, Svg.Ink, 1.4);
}
