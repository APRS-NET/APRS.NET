using System.CommandLine;
using System.Globalization;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace AprsNet.AprsIsCli;

/// <summary>
/// <c>--config FILE</c>: a command's options from a TOML file, for running <c>serve</c> or <c>tnc share</c> as a
/// service. Each key is an option's long name without the dashes (<c>listen = "0.0.0.0:14580"</c>), an array repeats
/// the option (<c>tnc = ["localhost:8001"]</c>), and <c>[tables]</c> only group the keys for the reader. An option
/// given on the command line wins over the file. <c>--check</c> reads everything and stops before connecting.
/// </summary>
internal static class ConfigFile
{
    /// <summary>Settings a file may not hold, and what to do instead.</summary>
    private static readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal)
    {
        ["config"] = "a config file can't name another one.",
        ["check"] = "give --check on the command line.",
        ["passcode"] =
            "keep the passcode out of the config file: set APRS_PASSCODE in the environment instead (the services "
            + "read it from /etc/aprsnet/secrets.env).",
    };

    /// <summary>The <c>--config</c> option, for a command that can run as a service.</summary>
    public static Option<string?> CreateOption() =>
        new("--config")
        {
            Description =
                "Read options from this TOML file: each key is an option's long name, such as listen = "
                + "\"0.0.0.0:14580\". Options on the command line win over the file.",
        };

    /// <summary>The <c>--check</c> option: check the options and the config file, and stop before connecting.</summary>
    public static Option<bool> CreateCheckOption() =>
        new("--check")
        {
            Description = "Check the options and any --config file, say what would run, and exit without connecting.",
        };

    /// <summary>
    /// When the parsed command was given <c>--config</c>, the arguments with the file's options added after them;
    /// otherwise <paramref name="args"/> as they are. False, with the reason, when the file can't be used.
    /// </summary>
    public static bool TryExpand(ParseResult parseResult, string[] args, out string[] expanded, out string error)
    {
        expanded = args;
        error = "";
        var command = parseResult.CommandResult.Command;
        if (
            command.Options.FirstOrDefault(o => o.Name == "--config") is not Option<string?> option
            || parseResult.GetValue(option) is not { } path
        )
        {
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"--config: can't read {path}: {ex.Message}";
            return false;
        }

        if (!TryRead(text, path, out var settings, out error))
        {
            return false;
        }

        var added = new List<string>();
        foreach (var setting in settings)
        {
            var where = $"{path}:{setting.Line}";
            if (_refused.TryGetValue(setting.Key, out var instead))
            {
                error = $"{where}: '{setting.Key}': {instead}";
                return false;
            }

            if (command.Options.FirstOrDefault(o => o.Name == $"--{setting.Key}") is not { } target)
            {
                error =
                    $"{where}: '{setting.Key}' is not an option of '{command.Name}'. See 'aprscli {Path(command)} --help'.";
                return false;
            }

            if (parseResult.GetResult(target) is { Implicit: false })
            {
                continue; // the command line wins
            }

            if (setting.Values.Count > 1 && target.Arity.MaximumNumberOfValues <= 1)
            {
                error = $"{where}: '{setting.Key}' takes one value, not a list.";
                return false;
            }

            // --name=value, so a value starting with '-' or '@' is never read as an option or a response file.
            added.AddRange(setting.Values.Select(v => $"{target.Name}={v}"));
        }

        expanded = [.. args, .. added];
        return true;
    }

    private static string Path(Command command)
    {
        var names = new List<string>();
        for (var c = command; c is not null and not RootCommand; c = c.Parents.OfType<Command>().FirstOrDefault())
        {
            names.Insert(0, c.Name);
        }

        return string.Join(' ', names);
    }

    /// <summary>One <c>key = value</c> line: its values as option text and its 1-based line.</summary>
    internal sealed record Setting(string Key, IReadOnlyList<string> Values, int Line);

    /// <summary>Reads the settings in a file's text, in order, checking that each key appears once.</summary>
    internal static bool TryRead(string text, string path, out List<Setting> settings, out string error)
    {
        settings = [];
        error = "";
        var document = SyntaxParser.Parse(text, path);
        if (document.HasErrors)
        {
            error = string.Join(
                Environment.NewLine,
                document
                    .Diagnostics.Where(d => d.Kind == DiagnosticMessageKind.Error)
                    .Select(d => $"{path}:{d.Span.Start.Line + 1}:{d.Span.Start.Column + 1}: {d.Message}")
            );
            return false;
        }

        var items = document.KeyValues.Select(kv => (Table: (string?)null, Item: kv)).ToList();
        foreach (var table in document.Tables)
        {
            if (table is TableArraySyntax)
            {
                error =
                    $"{path}:{Line(table)}: [[{table.Name}]]: a list of tables isn't a setting; use [{table.Name}].";
                return false;
            }

            items.AddRange(table.Items.Select(kv => (Table: (string?)Name(table.Name!), Item: kv)));
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (table, item) in items)
        {
            var line = Line(item);
            var key = Name(item.Key!);
            if (item.Key!.DotKeys.ChildrenCount > 0)
            {
                error = $"{path}:{line}: '{key}': dotted keys aren't settings; put the key in a [table] instead.";
                return false;
            }

            if (seen.TryGetValue(key, out var first))
            {
                error = $"{path}:{line}: '{key}' is already set on line {first}.";
                return false;
            }

            seen[key] = line;
            var values = new List<string>();
            if (item.Value is ArraySyntax array)
            {
                foreach (var element in array.Items)
                {
                    if (!TryFormat(element.Value!, out var value))
                    {
                        error = $"{path}:{line}: '{key}': a list may only hold strings, numbers and true or false.";
                        return false;
                    }

                    values.Add(value);
                }

                if (values.Count == 0)
                {
                    continue; // an empty list sets nothing
                }
            }
            else if (TryFormat(item.Value!, out var value))
            {
                values.Add(value);
            }
            else
            {
                var where = table is null ? "" : $" in [{table}]";
                error = $"{path}:{line}: '{key}'{where} must be a string, a number, true or false, or a list of them.";
                return false;
            }

            settings.Add(new Setting(key, values, line));
        }

        return true;
    }

    private static bool TryFormat(ValueSyntax value, out string text)
    {
        text = value switch
        {
            StringValueSyntax s => s.Value!,
            IntegerValueSyntax i => i.Value.ToString(CultureInfo.InvariantCulture),
            FloatValueSyntax f => f.Value.ToString("R", CultureInfo.InvariantCulture),
            BooleanValueSyntax b => b.Value ? "true" : "false",
            _ => null!,
        };
        return text is not null;
    }

    private static string Name(KeySyntax key) =>
        key.Key switch
        {
            BareKeySyntax bare => bare.Key!.Text!,
            StringValueSyntax quoted => quoted.Value!,
            var other => other!.ToString()!.Trim(),
        };

    private static int Line(SyntaxNode node) => node.Span.Start.Line + 1;
}
