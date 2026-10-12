using System.Globalization;

namespace AprsNet.AprsIsCli;

/// <summary>Parses short durations such as <c>90s</c>, <c>10m</c>, <c>2h</c> or <c>1d</c>. A bare number means seconds.</summary>
internal static class DurationParser
{
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        var unit = text[^1];
        var multiplier = char.ToLowerInvariant(unit) switch
        {
            's' => 1d,
            'm' => 60d,
            'h' => 3600d,
            'd' => 86400d,
            _ => 0d,
        };

        var number = multiplier == 0 ? text : text[..^1];
        if (multiplier == 0)
            multiplier = 1;

        if (
            !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value)
            || value <= 0
        )
            return false;

        var seconds = value * multiplier;
        if (seconds > TimeSpan.MaxValue.TotalSeconds / 2)
            return false;

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
