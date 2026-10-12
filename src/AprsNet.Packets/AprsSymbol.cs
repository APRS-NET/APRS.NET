namespace AprsNet.Packets;

/// <summary>
/// An APRS symbol as carried in position, object, item and Mic-E packets: a symbol table identifier and a symbol code.
/// The table is <c>/</c> (primary), <c>\</c> (alternate), or an overlay character <c>0</c>–<c>9</c> or <c>A</c>–<c>Z</c>,
/// which selects the alternate table with that character drawn on the symbol (APRS 1.1 and 1.2).
/// </summary>
public readonly record struct AprsSymbol
{
    /// <summary>Creates a symbol from its table identifier and code.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="table"/> is not <c>/</c>, <c>\</c>, <c>0</c>–<c>9</c> or <c>A</c>–<c>Z</c>, or
    /// <paramref name="code"/> is not a printable ASCII character (<c>!</c> to <c>~</c>).
    /// </exception>
    public AprsSymbol(char table, char code)
    {
        if (!IsTable(table))
        {
            throw new ArgumentOutOfRangeException(
                nameof(table),
                table,
                "The symbol table must be '/', '\\', 0-9 or A-Z."
            );
        }

        if (!IsCode(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The symbol code must be a printable ASCII character."
            );
        }

        Table = table;
        Code = code;
    }

    /// <summary>The symbol table identifier as transmitted: <c>/</c>, <c>\</c>, or an overlay character.</summary>
    public char Table { get; }

    /// <summary>The symbol code, <c>!</c> to <c>~</c>.</summary>
    public char Code { get; }

    /// <summary>Whether the symbol is from the primary table (<c>/</c>).</summary>
    public bool IsPrimary => Table == '/';

    /// <summary>The overlay character drawn on an alternate-table symbol, or <see langword="null"/>.</summary>
    public char? Overlay => char.IsAsciiDigit(Table) || char.IsAsciiLetterUpper(Table) ? Table : null;

    /// <summary>The same symbol without its overlay: alternate-table symbols move to table <c>\</c>.</summary>
    public AprsSymbol WithoutOverlay => Overlay is null ? this : new AprsSymbol('\\', Code);

    /// <summary>Creates a symbol, or returns <see langword="false"/> when the table or code is not valid.</summary>
    public static bool TryCreate(char table, char code, out AprsSymbol symbol)
    {
        symbol = IsTable(table) && IsCode(code) ? new AprsSymbol(table, code) : default;
        return symbol != default;
    }

    /// <summary>The table and code as written in APRS documents, for example <c>/&gt;</c> or <c>E0</c>.</summary>
    public override string ToString() => string.Concat(Table.ToString(), Code.ToString());

    private static bool IsTable(char table) =>
        table is '/' or '\\' || char.IsAsciiDigit(table) || char.IsAsciiLetterUpper(table);

    private static bool IsCode(char code) => code is >= '!' and <= '~';
}
