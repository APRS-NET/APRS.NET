using System.Globalization;

namespace AprsNet.Packets;

/// <summary>Which of the APRS timestamp formats a timestamp was sent in.</summary>
public enum AprsTimestampFormat
{
    /// <summary>Day of the month, hour and minute: <c>DDHHMMz</c> (zulu) or <c>DDHHMM/</c> (local).</summary>
    DayHourMinute,

    /// <summary>Hour, minute and second, always zulu: <c>HHMMSSh</c>.</summary>
    HourMinuteSecond,

    /// <summary>Month, day, hour and minute, always zulu: <c>MMDDHHMM</c>, used by positionless weather reports.</summary>
    MonthDayHourMinute,
}

/// <summary>
/// A timestamp as sent in a packet. APRS timestamps carry no year (most carry no month, and the hour-minute-second
/// form no day), so <see cref="ToDateTimeOffset"/> places them relative to when the packet was received.
/// </summary>
public readonly record struct AprsTimestamp
{
    private AprsTimestamp(
        AprsTimestampFormat format,
        int? day,
        int hour,
        int minute,
        int? second,
        bool isLocal,
        int? month = null
    )
    {
        Format = format;
        Month = month;
        Day = day;
        Hour = hour;
        Minute = minute;
        Second = second;
        IsLocal = isLocal;
    }

    /// <summary>The format the timestamp was sent in.</summary>
    public AprsTimestampFormat Format { get; }

    /// <summary>Month, 1 to 12, for <see cref="AprsTimestampFormat.MonthDayHourMinute"/>.</summary>
    public int? Month { get; }

    /// <summary>
    /// Day of the month, 1 to 31, for <see cref="AprsTimestampFormat.DayHourMinute"/> and
    /// <see cref="AprsTimestampFormat.MonthDayHourMinute"/>.
    /// </summary>
    public int? Day { get; }

    /// <summary>Hour, 0 to 23.</summary>
    public int Hour { get; }

    /// <summary>Minute, 0 to 59.</summary>
    public int Minute { get; }

    /// <summary>Second, 0 to 59, for <see cref="AprsTimestampFormat.HourMinuteSecond"/>.</summary>
    public int? Second { get; }

    /// <summary>Whether the sender's local time was used (<c>/</c>) instead of zulu (UTC). APRS discourages it.</summary>
    public bool IsLocal { get; }

    /// <summary>
    /// The full UTC time, taken as the latest matching time at or shortly before <paramref name="received"/> (a
    /// sender's clock may run up to a day ahead). Returns <see langword="null"/> for local times, whose time zone is
    /// unknown, and for a day that does not exist in the months concerned.
    /// </summary>
    public DateTimeOffset? ToDateTimeOffset(DateTimeOffset received)
    {
        if (IsLocal)
        {
            return null;
        }

        var utc = received.ToUniversalTime();
        if (Format == AprsTimestampFormat.HourMinuteSecond)
        {
            var sameDay = new DateTimeOffset(utc.Year, utc.Month, utc.Day, Hour, Minute, Second!.Value, TimeSpan.Zero);
            return sameDay - utc > TimeSpan.FromHours(1) ? sameDay.AddDays(-1) : sameDay;
        }

        if (Format == AprsTimestampFormat.MonthDayHourMinute)
        {
            // This year, or last year when that would put the time more than a day ahead.
            for (var year = utc.Year; year >= utc.Year - 1; year--)
            {
                if (Day > DateTime.DaysInMonth(year, Month!.Value))
                {
                    continue;
                }

                var candidate = new DateTimeOffset(year, Month.Value, Day!.Value, Hour, Minute, 0, TimeSpan.Zero);
                if (candidate - utc <= TimeSpan.FromDays(1))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Day-hour-minute: this month, or the previous one when that would put the time more than a day ahead.
        for (var monthsBack = 0; monthsBack <= 2; monthsBack++)
        {
            var month = new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsBack);
            if (Day > DateTime.DaysInMonth(month.Year, month.Month))
            {
                continue;
            }

            var candidate = new DateTimeOffset(month.Year, month.Month, Day!.Value, Hour, Minute, 0, TimeSpan.Zero);
            if (candidate - utc <= TimeSpan.FromDays(1))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The timestamp in its wire form, for example <c>092345z</c>, <c>234517h</c> or <c>10092345</c>.</summary>
    public override string ToString() =>
        Format switch
        {
            AprsTimestampFormat.HourMinuteSecond => string.Create(
                CultureInfo.InvariantCulture,
                $"{Hour:00}{Minute:00}{Second:00}h"
            ),
            AprsTimestampFormat.MonthDayHourMinute => string.Create(
                CultureInfo.InvariantCulture,
                $"{Month:00}{Day:00}{Hour:00}{Minute:00}"
            ),
            _ => string.Create(CultureInfo.InvariantCulture, $"{Day:00}{Hour:00}{Minute:00}{(IsLocal ? '/' : 'z')}"),
        };

    /// <summary>Parses the 8-digit <c>MMDDHHMM</c> timestamp at the start of <paramref name="bytes"/>.</summary>
    internal static bool TryParseMonthDay(ReadOnlySpan<byte> bytes, out AprsTimestamp timestamp)
    {
        timestamp = default;
        if (
            bytes.Length < 8
            || !TwoDigits(bytes, 0, out var month)
            || !TwoDigits(bytes, 2, out var day)
            || !TwoDigits(bytes, 4, out var hour)
            || !TwoDigits(bytes, 6, out var minute)
            || month is < 1 or > 12
            || day is < 1 or > 31
            || hour > 23
            || minute > 59
        )
        {
            return false;
        }

        timestamp = new AprsTimestamp(AprsTimestampFormat.MonthDayHourMinute, day, hour, minute, null, false, month);
        return true;
    }

    /// <summary>Parses the 7-byte timestamp at the start of <paramref name="bytes"/>.</summary>
    internal static bool TryParse(ReadOnlySpan<byte> bytes, out AprsTimestamp timestamp)
    {
        timestamp = default;
        if (
            bytes.Length < 7
            || !TwoDigits(bytes, 0, out var a)
            || !TwoDigits(bytes, 2, out var b)
            || !TwoDigits(bytes, 4, out var c)
        )
        {
            return false;
        }

        switch (bytes[6])
        {
            case (byte)'z' or (byte)'/' when a is >= 1 and <= 31 && b <= 23 && c <= 59:
                timestamp = new AprsTimestamp(AprsTimestampFormat.DayHourMinute, a, b, c, null, bytes[6] == '/');
                return true;
            case (byte)'h' when a <= 23 && b <= 59 && c <= 59:
                timestamp = new AprsTimestamp(AprsTimestampFormat.HourMinuteSecond, null, a, b, c, false);
                return true;
            default:
                return false;
        }
    }

    private static bool TwoDigits(ReadOnlySpan<byte> bytes, int at, out int value)
    {
        value = ((bytes[at] - '0') * 10) + (bytes[at + 1] - '0');
        return char.IsAsciiDigit((char)bytes[at]) && char.IsAsciiDigit((char)bytes[at + 1]);
    }
}
