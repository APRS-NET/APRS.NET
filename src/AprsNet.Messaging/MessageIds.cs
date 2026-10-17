namespace AprsNet.Messaging;

/// <summary>
/// Two-character message IDs from <c>0-9A-Za-z</c>, so a reply-ack ID (<c>{MM}AA</c>) fits APRS's five characters.
/// </summary>
internal static class MessageIds
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static bool IsValid(string id) => id.Length == 2 && Digits.Contains(id[0]) && Digits.Contains(id[1]);

    /// <summary>The ID after <paramref name="last"/>: <c>01</c> first, and after <c>zz</c> back to <c>01</c>.</summary>
    public static string Next(string? last)
    {
        var value = last is null ? 0 : (Digits.IndexOf(last[0]) * Digits.Length) + Digits.IndexOf(last[1]);
        value = (value + 1) % (Digits.Length * Digits.Length);
        if (value == 0)
        {
            value = 1;
        }

        return string.Concat(Digits[value / Digits.Length], Digits[value % Digits.Length]);
    }
}
