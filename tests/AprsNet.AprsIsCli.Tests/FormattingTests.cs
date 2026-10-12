namespace AprsNet.AprsIsCli.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData("90s", 90)]
    [InlineData("90", 90)]
    [InlineData("10m", 600)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("0.5s", 0.5)]
    [InlineData(" 5M ", 300)]
    public void Duration_parses(string text, double seconds)
    {
        Assert.True(DurationParser.TryParse(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("0")]
    [InlineData("-1m")]
    [InlineData("soon")]
    [InlineData("1x")]
    [InlineData("NaN")]
    [InlineData("1e308d")]
    public void Duration_rejects(string? text) => Assert.False(DurationParser.TryParse(text, out _));

    [Fact]
    public void Timestamp_is_iso_8601_utc_with_milliseconds() =>
        Assert.Equal(
            "2026-10-11T07:00:00.005Z",
            LineFormatter.Timestamp(new DateTimeOffset(2026, 10, 11, 2, 0, 0, 5, TimeSpan.FromHours(-5)))
        );

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("a\u001bb", "a\\x1Bb")]
    [InlineData("tab\there", "tab\\x09here")]
    [InlineData("\u007f\u0080\u009f", "\\x7F\\x80\\x9F")]
    [InlineData("café ÿ", "café ÿ")]
    public void Console_escaping(string raw, string expected) =>
        Assert.Equal(expected, LineFormatter.EscapeForConsole(raw));
}
