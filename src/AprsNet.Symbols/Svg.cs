using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AprsNet.Symbols;

/// <summary>Builds one 48×48 SVG. Every drawing uses the same grid, outline colour and line weights.</summary>
internal sealed partial class Svg
{
    public const string Ink = "#1b1b1b";
    private const string Paper = "#ffffff";

    // Fills that are details (glass, screens, wheel hubs): cut out of a single-colour silhouette.
    private static readonly HashSet<string> _details = new(StringComparer.OrdinalIgnoreCase)
    {
        "#bfe3ff",
        "#74c0fc",
        "#c9c9c9",
        "#d3f9d8",
    };

    private readonly StringBuilder _body = new();

    /// <summary>
    /// When set, drawings become white silhouettes: outlines and halos are dropped, details are cut out in this
    /// colour, and containers (<see cref="Backdrop"/>) are skipped because the caller supplies its own.
    /// </summary>
    public string? Mono { get; set; }

    private const string CutPrefix = "cut:";

    /// <summary>
    /// Marks a colour as a detail: drawn as given in colour styles, cut out of the silhouette in single-colour ones.
    /// </summary>
    public static string Cut(string color) => CutPrefix + color;

    private string F(string fill) =>
        fill.StartsWith(CutPrefix, StringComparison.Ordinal) ? Mono ?? fill[CutPrefix.Length..]
        : Mono is null || fill == "none" ? fill
        : _details.Contains(fill) ? Mono
        : Paper;

    private string? S(string? stroke, string fill) =>
        stroke is null ? null
        : Mono is null || fill == "none" ? F(stroke)
        : null;

    /// <summary>A container shape (badge square, label disc) that a single-colour style replaces with its own.</summary>
    public Svg Backdrop(Func<Svg, Svg> draw) => Mono is null ? draw(this) : this;

    public Svg Path(string d, string fill, string? stroke = Ink, double width = 1.6)
    {
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<path d=\"{d}\" fill=\"{F(fill)}\"{Stroke(S(stroke, fill), width)}/>"
        );
        return this;
    }

    /// <summary>Round-capped lines; a halo draws the same lines wider underneath so thin line art reads on any map.</summary>
    public Svg Line(string d, string stroke, double width, double? halo = null, string haloColor = Ink)
    {
        stroke = F(stroke);
        if (halo is { } h && Mono is null)
        {
            _body.Append(
                CultureInfo.InvariantCulture,
                $"<path d=\"{d}\" fill=\"none\" stroke=\"{haloColor}\" stroke-width=\"{N(width + h)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"
            );
        }

        _body.Append(
            CultureInfo.InvariantCulture,
            $"<path d=\"{d}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{N(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"
        );
        return this;
    }

    public Svg Circle(double cx, double cy, double r, string fill, string? stroke = Ink, double width = 1.6)
    {
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\" fill=\"{F(fill)}\"{Stroke(S(stroke, fill), width)}/>"
        );
        return this;
    }

    public Svg Rect(double x, double y, double w, double h, string fill, string? stroke = Ink, double rx = 0)
    {
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\"{(rx > 0 ? $" rx=\"{N(rx)}\"" : "")} fill=\"{F(fill)}\"{Stroke(S(stroke, fill), 1.6)}/>"
        );
        return this;
    }

    public Svg Polygon(IEnumerable<(double X, double Y)> points, string fill, string? stroke = Ink, double width = 1.6)
    {
        var p = string.Join(' ', points.Select(pt => $"{N(pt.X)},{N(pt.Y)}"));
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<polygon points=\"{p}\" fill=\"{F(fill)}\"{Stroke(S(stroke, fill), width)}/>"
        );
        return this;
    }

    public Svg Star(
        double cx,
        double cy,
        double outer,
        double inner,
        string fill,
        int points = 5,
        string? stroke = Ink,
        double width = 1.6
    ) =>
        Polygon(
            Enumerable
                .Range(0, points * 2)
                .Select(i =>
                {
                    var angle = (-90 + (180.0 / points * i)) * Math.PI / 180;
                    var r = i % 2 == 0 ? outer : inner;
                    return (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
                }),
            fill,
            stroke,
            width
        );

    /// <summary>
    /// Text in the stroke font, centred on (cx, cy), with a contrasting halo so it reads on any fill. In a
    /// single-colour style it is drawn larger (the glyph is scaled down with the drawing) and white, or cut out when
    /// <paramref name="cut"/> says it sits on a white shape.
    /// </summary>
    public Svg Text(string text, double cx, double cy, double height, bool light = true, bool cut = false)
    {
        if (Mono is not null)
        {
            height *= 1.45;
        }

        var d = StrokeFont.Path(text, cx, cy, height);
        var width = height * 0.17;
        if (Mono is not null)
        {
            _body.Append(
                CultureInfo.InvariantCulture,
                $"<path d=\"{d}\" fill=\"none\" stroke=\"{(cut ? Mono : Paper)}\" stroke-width=\"{N(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"
            );
            return this;
        }

        var (glyph, halo) = light ? ("#ffffff", Ink) : (Ink, "#ffffff");
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<path d=\"{d}\" fill=\"none\" stroke=\"{halo}\" stroke-width=\"{N(width * (light ? 2.1 : 1.5))}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"
        );
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<path d=\"{d}\" fill=\"none\" stroke=\"{glyph}\" stroke-width=\"{N(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"
        );
        return this;
    }

    /// <summary>An unfilled circle, with a halo underneath so it reads on any map.</summary>
    public Svg Ring(double cx, double cy, double r, string stroke, double width, string halo = "#ffffff")
    {
        if (Mono is not null)
        {
            _body.Append(
                CultureInfo.InvariantCulture,
                $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\" fill=\"none\" stroke=\"{Paper}\" stroke-width=\"{N(width)}\"/>"
            );
            return this;
        }

        _body.Append(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\" fill=\"none\" stroke=\"{halo}\" stroke-width=\"{N(width + 2)}\"/>"
        );
        _body.Append(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{N(width)}\"/>"
        );
        return this;
    }

    /// <summary>Draws the following elements inside a transform (rotate, translate, scale) until <see cref="Close"/>.</summary>
    public Svg Open(string transform)
    {
        _body.Append(CultureInfo.InvariantCulture, $"<g transform=\"{transform}\">");
        return this;
    }

    public Svg Close()
    {
        _body.Append("</g>");
        return this;
    }

    /// <summary>An element the helpers do not cover (dashed outlines, translucent fills).</summary>
    public Svg Raw(string element)
    {
        _body.Append(Mono is null ? element : HexColor().Replace(element, m => F(m.Value)));
        return this;
    }

    [GeneratedRegex("#[0-9a-fA-F]{6}")]
    private static partial Regex HexColor();

    public string Build() =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 48 48\" width=\"48\" height=\"48\">{_body}</svg>\n";

    internal static string N(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    private static string Stroke(string? stroke, double width) =>
        stroke is null ? "" : $" stroke=\"{stroke}\" stroke-width=\"{N(width)}\" stroke-linejoin=\"round\"";
}
