using System.Globalization;
using System.Text;

namespace AprsNet.Symbols;

/// <summary>
/// A monoline font for overlay characters and the few labels symbols carry: each glyph is polylines on a 4×6 grid
/// (x right, y down), drawn as round-capped strokes.
/// </summary>
internal static class StrokeFont
{
    private const string Oval = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0";

    private static readonly Dictionary<char, string> _glyphs = new()
    {
        ['A'] = "0,6 0,2 2,0 4,2 4,6; 0,3.6 4,3.6",
        ['B'] = "0,3 3,3 4,4 4,5 3,6 0,6 0,0 3,0 4,1 4,2 3,3",
        ['C'] = "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5",
        ['D'] = "0,0 2.5,0 4,1.5 4,4.5 2.5,6 0,6 0,0",
        ['E'] = "4,0 0,0 0,6 4,6; 0,3 3,3",
        ['F'] = "4,0 0,0 0,6; 0,3 3,3",
        ['G'] = "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5 4,3.5 2.4,3.5",
        ['H'] = "0,0 0,6; 4,0 4,6; 0,3 4,3",
        ['I'] = "1,0 3,0; 2,0 2,6; 1,6 3,6",
        ['J'] = "1.5,0 4,0; 3,0 3,5 2,6 1,6 0,5",
        ['K'] = "0,0 0,6; 4,0 0,3.6; 1.3,2.8 4,6",
        ['L'] = "0,0 0,6 4,6",
        ['M'] = "0,6 0,0 2,3 4,0 4,6",
        ['N'] = "0,6 0,0 4,6 4,0",
        ['O'] = Oval,
        ['P'] = "0,6 0,0 3,0 4,1 4,2 3,3 0,3",
        ['Q'] = Oval + "; 2.4,4.4 4,6",
        ['R'] = "0,6 0,0 3,0 4,1 4,2 3,3 0,3; 2,3 4,6",
        ['S'] = "4,1 3,0 1,0 0,1 0,2 1,3 3,3 4,4 4,5 3,6 1,6 0,5",
        ['T'] = "0,0 4,0; 2,0 2,6",
        ['U'] = "0,0 0,5 1,6 3,6 4,5 4,0",
        ['V'] = "0,0 2,6 4,0",
        ['W'] = "0,0 1,6 2,3 3,6 4,0",
        ['X'] = "0,0 4,6; 4,0 0,6",
        ['Y'] = "0,0 2,3 4,0; 2,3 2,6",
        ['Z'] = "0,0 4,0 0,6 4,6",
        ['0'] = Oval + "; 0.8,4.6 3.2,1.4",
        ['1'] = "0.8,1.2 2,0 2,6; 0.8,6 3.2,6",
        ['2'] = "0,1 1,0 3,0 4,1 4,2 0,6 4,6",
        ['3'] = "0,1 1,0 3,0 4,1 4,2 3,3 4,4 4,5 3,6 1,6 0,5; 1.6,3 3,3",
        ['4'] = "3,6 3,0 0,4 4,4",
        ['5'] = "4,0 0,0 0,3 3,3 4,4 4,5 3,6 1,6 0,5",
        ['6'] = "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5 4,4 3,3 0,3",
        ['7'] = "0,0 4,0 1.4,6",
        ['8'] = "1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3 1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3",
        ['9'] = "4,3 1,3 0,2 0,1 1,0 3,0 4,1 4,5 3,6 1,6 0,5",
        ['?'] = "0,1 1,0 3,0 4,1 4,2 2,3.6 2,4.3; 2,5.9 2,6",
        ['!'] = "2,0 2,4.2; 2,5.9 2,6",
        ['$'] = "4,1.2 3,0.6 1,0.6 0,1.4 0,2.2 1,3 3,3 4,3.8 4,4.6 3,5.4 1,5.4 0,4.8; 2,-0.3 2,6.3",
    };

    public static string Path(string text, double cx, double cy, double height)
    {
        var scale = height / 6;
        const double advance = 5.6; // glyph width 4 plus spacing
        var width = (text.Length * advance) - 1.6;
        var x0 = cx - (width * scale / 2);
        var y0 = cy - (height / 2);
        var d = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            foreach (var stroke in _glyphs[char.ToUpperInvariant(text[i])].Split(';'))
            {
                var points = stroke.Trim().Split(' ');
                for (var p = 0; p < points.Length; p++)
                {
                    var xy = points[p].Split(',');
                    var x = x0 + ((double.Parse(xy[0], CultureInfo.InvariantCulture) + (i * advance)) * scale);
                    var y = y0 + (double.Parse(xy[1], CultureInfo.InvariantCulture) * scale);
                    d.Append(p == 0 ? 'M' : 'L').Append(Svg.N(x)).Append(',').Append(Svg.N(y));
                }
            }
        }

        return d.ToString();
    }

    public static bool Has(char c) => _glyphs.ContainsKey(char.ToUpperInvariant(c));
}
