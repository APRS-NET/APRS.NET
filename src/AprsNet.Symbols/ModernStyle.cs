namespace AprsNet.Symbols;

/// <summary>
/// The modern style: a white glyph on a disc coloured by the symbol's category, so the kind of station shows at a
/// glance. Overlay families keep their shape (star, diamond, circle, box, triangle) as the coloured container. Glyphs
/// are the conventional drawings rendered as single-colour silhouettes, unless a symbol has its own modern drawing.
/// </summary>
internal sealed class ModernStyle(ConventionalStyle shapes) : ISymbolStyle
{
    private const string _white = "#ffffff";

    private static readonly Dictionary<AprsSymbolCategory, string> _colors = new()
    {
        [AprsSymbolCategory.Radio] = "#2b8a3e",
        [AprsSymbolCategory.Vehicles] = "#1971c2",
        [AprsSymbolCategory.Aircraft] = "#862e9c",
        [AprsSymbolCategory.Marine] = "#0b7285",
        [AprsSymbolCategory.Emergency] = "#c92a2a",
        [AprsSymbolCategory.Weather] = "#52667a",
        [AprsSymbolCategory.Places] = "#d9480f",
        [AprsSymbolCategory.People] = "#a61e4d",
        [AprsSymbolCategory.Markers] = "#343a40",
    };

    // Overlay families drawn as a coloured shape instead of a disc, with where the overlay character goes.
    private static readonly Dictionary<string, (Action<Svg, string> Draw, double X, double Y, double H)> _containers =
        new(StringComparer.Ordinal)
        {
            ["/#"] = ((s, c) => Star(s, c).Circle(24, 25.5, 5.5, _white, null), 24, 26, 14),
            ["\\#"] = ((s, c) => Star(s, c), 24, 26, 14),
            ["\\_"] = ((s, c) => Star(s, c), 24, 26, 14),
            ["/&"] = ((s, c) => Diamond(s, c).Text("HF", 24, 24, 10), 24, 24, 16),
            ["\\&"] = ((s, c) => Diamond(s, c), 24, 24, 16),
            ["\\a"] = ((s, c) => Diamond(s, c), 24, 24, 16),
            ["\\0"] = ((s, c) => s.Circle(24, 24, 21, c, _white, 2), 24, 24, 18),
            ["\\W"] = ((s, c) => s.Circle(24, 24, 21, c, _white, 2), 24, 24, 18),
            ["\\A"] = ((s, c) => s.Rect(4, 4, 40, 40, c, _white, rx: 6), 24, 24, 18),
            ["\\i"] = ((s, c) => s.Rect(4, 4, 40, 40, c, _white, rx: 6), 24, 24, 18),
            ["\\n"] = (Triangle, 24, 31, 14),
            ["\\c"] = (Triangle, 24, 31, 14),
        };

    // Shown on a container when no overlay character takes its place.
    private static readonly Dictionary<string, Action<Svg>> _containerLabels = new(StringComparer.Ordinal)
    {
        ["\\W"] = s => s.Text("NWS", 24, 24, 11),
        ["\\c"] = s => s.Text("CD", 24, 31, 10),
        ["\\i"] = s => s.Circle(24, 24, 4.5, _white, null),
    };

    public string? Draw(string key, AprsSymbolCategory category, char? overlay)
    {
        if (!shapes.TryGetDrawing(key, out var draw, out var label))
        {
            return null;
        }

        var color = _colors[category];
        var svg = new Svg();
        var hasOverlay = overlay is { } o && StrokeFont.Has(o);
        if (_containers.TryGetValue(key, out var container))
        {
            container.Draw(svg, color);
            if (hasOverlay)
            {
                svg.Text(overlay!.Value.ToString(), container.X, container.Y, container.H);
            }
            else if (_containerLabels.TryGetValue(key, out var containerLabel))
            {
                containerLabel(svg);
            }

            return svg.Build();
        }

        svg.Circle(24, 24, 21, color, _white, 2).Open("translate(24 24) scale(0.6) translate(-24 -24)");
        svg.Mono = color;
        draw(svg);
        if (!hasOverlay)
        {
            label?.Invoke(svg);
        }

        svg.Mono = null;
        svg.Close();
        if (hasOverlay)
        {
            svg.Circle(38.5, 38.5, 8.5, _white, color, 1.8).Text(overlay!.Value.ToString(), 38.5, 38.5, 9.5, false);
        }

        return svg.Build();
    }

    public string Unassigned() =>
        new Svg()
            .Circle(24, 24, 21, "#868e96", _white, 2)
            .Ring(24, 24, 10, _white, 3.4)
            .Line("M17,17 L31,31", _white, 3.4)
            .Build();

    private static Svg Star(Svg s, string color) => s.Star(24, 25.5, 22.5, 10, color, stroke: _white, width: 2);

    private static Svg Diamond(Svg s, string color) =>
        s.Polygon([(24, 2), (46, 24), (24, 46), (2, 24)], color, _white, 2);

    private static void Triangle(Svg s, string color) => s.Polygon([(24, 3), (46, 43), (2, 43)], color, _white, 2);
}
