namespace AprsNet.Packets;

/// <summary>
/// Input a person typed (a callsign, a path entry, a server address, a filter) was refused. It is an
/// <see cref="ArgumentException"/>, so existing <c>catch (ArgumentException)</c> blocks still see it; the
/// <see cref="Code"/> says what was wrong in a form an app can translate, and <see cref="Index"/> and
/// <see cref="Character"/> point at the offending character where there is one. The English
/// <see cref="Exception.Message"/> is for logs and may be reworded; the code is not.
/// </summary>
public sealed class AprsValidationException : ArgumentException
{
    /// <summary>Creates the exception; <see cref="Character"/> is taken from <paramref name="value"/> at <paramref name="index"/>.</summary>
    /// <param name="code">What was wrong.</param>
    /// <param name="message">An English sentence for logs.</param>
    /// <param name="paramName">The parameter or property that held the input.</param>
    /// <param name="value">The refused input.</param>
    /// <param name="index">The position of the offending character in <paramref name="value"/>, if one is at fault.</param>
    public AprsValidationException(
        AprsValidationErrorCode code,
        string message,
        string? paramName,
        string? value,
        int? index = null
    )
        : base(message, paramName)
    {
        Code = code;
        Value = value;
        Index = index;
        Character = index is { } i && value is not null && i < value.Length ? value[i] : null;
    }

    /// <summary>What was wrong.</summary>
    public AprsValidationErrorCode Code { get; }

    /// <summary>The refused input as given, for example the callsign or the server address text.</summary>
    public string? Value { get; }

    /// <summary>The zero-based position of the offending character in <see cref="Value"/>, when one is at fault.</summary>
    public int? Index { get; }

    /// <summary>The offending character, <c>Value[Index]</c>, when one is at fault.</summary>
    public char? Character { get; }
}
