using System.Xml.Linq;
using AprsNet.Packets;

namespace AprsNet.AprsIsTcpClient.Tests;

/// <summary>
/// Every refusal of input a person typed carries an <see cref="AprsValidationErrorCode"/>, so an app can translate it.
/// </summary>
public class ValidationCodeTests
{
    private sealed record Case(
        Action Act,
        AprsValidationErrorCode Code,
        string ParamName,
        int? Index = null,
        char? Character = null
    );

    private static AprsIsClientOptions Options(
        Action<AprsIsClientOptions> configure,
        string softwareName = "test",
        string softwareVersion = "1.0"
    )
    {
        var options = new AprsIsClientOptions
        {
            Callsign = "N0CALL",
            SoftwareName = softwareName,
            SoftwareVersion = softwareVersion,
        };
        configure(options);
        return options;
    }

    private static readonly Dictionary<string, Case> _cases = new()
    {
        // AprsIsClientOptions.Validate
        ["Options: empty callsign"] = new(
            () => Options(o => o.Callsign = "").Validate(),
            AprsValidationErrorCode.CallsignInvalidLength,
            "Callsign"
        ),
        ["Options: callsign over 9 characters"] = new(
            () => Options(o => o.Callsign = "N0CALL-100").Validate(),
            AprsValidationErrorCode.CallsignInvalidLength,
            "Callsign"
        ),
        ["Options: callsign with a slash"] = new(
            () => Options(o => o.Callsign = "N0CALL/M").Validate(),
            AprsValidationErrorCode.CallsignInvalidCharacter,
            "Callsign",
            6,
            '/'
        ),
        ["Options: empty software name"] = new(
            () => Options(_ => { }, softwareName: "").Validate(),
            AprsValidationErrorCode.SoftwareNameInvalid,
            "SoftwareName"
        ),
        ["Options: software name with a space"] = new(
            () => Options(_ => { }, softwareName: "My App").Validate(),
            AprsValidationErrorCode.SoftwareNameInvalid,
            "SoftwareName",
            2,
            ' '
        ),
        ["Options: software version with a tab"] = new(
            () => Options(_ => { }, softwareVersion: "1.0\t").Validate(),
            AprsValidationErrorCode.SoftwareVersionInvalid,
            "SoftwareVersion",
            3,
            '\t'
        ),
        ["Options: empty software version"] = new(
            () => Options(_ => { }, softwareVersion: "").Validate(),
            AprsValidationErrorCode.SoftwareVersionInvalid,
            "SoftwareVersion"
        ),
        ["Options: filter with a line break"] = new(
            () => Options(o => o.Filter = "r/35/-97/200\nuser X").Validate(),
            AprsValidationErrorCode.FilterControlCharacter,
            "Filter",
            12,
            '\n'
        ),
        // AprsIsPasscode.Compute
        ["Passcode: empty base callsign"] = new(
            () => AprsIsPasscode.Compute("-9"),
            AprsValidationErrorCode.CallsignInvalidLength,
            "callsign"
        ),
        ["Passcode: base callsign with a space"] = new(
            () => AprsIsPasscode.Compute("N0 CALL-9"),
            AprsValidationErrorCode.CallsignInvalidCharacter,
            "callsign",
            2,
            ' '
        ),
        // Callsign.Parse
        ["Callsign: empty"] = new(() => Callsign.Parse(""), AprsValidationErrorCode.CallsignInvalidLength, "value"),
        ["Callsign: header separator"] = new(
            () => Callsign.Parse("N0CALL>"),
            AprsValidationErrorCode.CallsignInvalidCharacter,
            "value",
            6,
            '>'
        ),
        // PathEntry.Parse
        ["Path entry: too long"] = new(
            () => PathEntry.Parse("TOOLONGALIAS"),
            AprsValidationErrorCode.PathEntryInvalidLength,
            "entry"
        ),
        ["Path entry: only the repeated marker"] = new(
            () => PathEntry.Parse("*"),
            AprsValidationErrorCode.PathEntryInvalidLength,
            "entry"
        ),
        ["Path entry: a space"] = new(
            () => PathEntry.Parse("WIDE 1"),
            AprsValidationErrorCode.PathEntryInvalidCharacter,
            "entry",
            4,
            ' '
        ),
        // PacketAddress.Parse
        ["Address: source base over 6 characters"] = new(
            () => PacketAddress.Parse("N0CALLX-9", "APZ123", []),
            AprsValidationErrorCode.CallsignInvalidLength,
            "source"
        ),
        ["Address: source with a used flag"] = new(
            () => PacketAddress.Parse("N0CALL*", "APZ123", []),
            AprsValidationErrorCode.CallsignInvalidCharacter,
            "source",
            6,
            '*'
        ),
        ["Address: source SSID too long"] = new(
            () => PacketAddress.Parse("N0CAL-123", "APZ123", []),
            AprsValidationErrorCode.CallsignInvalidSsid,
            "source",
            6,
            '1'
        ),
        ["Address: source SSID with a dot"] = new(
            () => PacketAddress.Parse("N0CALL-1.", "APZ123", []),
            AprsValidationErrorCode.CallsignInvalidSsid,
            "source",
            7,
            '1'
        ),
        ["Address: destination not a TOCALL"] = new(
            () => PacketAddress.Parse("N0CALL-9", "apz123", []),
            AprsValidationErrorCode.DestinationInvalid,
            "destination"
        ),
        ["Address: bad path entry"] = new(
            () => PacketAddress.Parse("N0CALL-9", "APZ123", ["WIDE1-1", "WIDE 2"]),
            AprsValidationErrorCode.PathEntryInvalidCharacter,
            "path",
            4,
            ' '
        ),
        ["Address: path too long"] = new(
            () => PacketAddress.Parse("N0CALL-9", "APZ123", Enumerable.Repeat("WIDE1-1", 11)),
            AprsValidationErrorCode.PathTooLong,
            "path"
        ),
        // AprsIsEndpoint
        ["Endpoint: blank host"] = new(
            () => _ = new AprsIsEndpoint(" "),
            AprsValidationErrorCode.ServerHostEmpty,
            "host"
        ),
        ["Endpoint: port 0"] = new(
            () => _ = new AprsIsEndpoint("rotate.aprs2.net", 0),
            AprsValidationErrorCode.ServerPortOutOfRange,
            "port"
        ),
        ["Endpoint parse: empty"] = new(
            () => AprsIsEndpoint.Parse(""),
            AprsValidationErrorCode.ServerHostEmpty,
            "text"
        ),
        ["Endpoint parse: no host before the port"] = new(
            () => AprsIsEndpoint.Parse(":14580"),
            AprsValidationErrorCode.ServerHostEmpty,
            "text"
        ),
        ["Endpoint parse: unclosed bracket"] = new(
            () => AprsIsEndpoint.Parse("[2001:db8::1:14580"),
            AprsValidationErrorCode.ServerHostInvalid,
            "text",
            0,
            '['
        ),
        ["Endpoint parse: text after the bracket"] = new(
            () => AprsIsEndpoint.Parse("[2001:db8::1]x"),
            AprsValidationErrorCode.ServerHostInvalid,
            "text",
            13,
            'x'
        ),
        ["Endpoint parse: IPv6 without brackets"] = new(
            () => AprsIsEndpoint.Parse("2001:db8::1"),
            AprsValidationErrorCode.ServerHostInvalid,
            "text",
            9,
            ':'
        ),
        ["Endpoint parse: space inside the host"] = new(
            () => AprsIsEndpoint.Parse("rotate aprs2.net"),
            AprsValidationErrorCode.ServerHostInvalid,
            "text",
            6,
            ' '
        ),
        ["Endpoint parse: port is not a number"] = new(
            () => AprsIsEndpoint.Parse("rotate.aprs2.net:14a80"),
            AprsValidationErrorCode.ServerPortInvalid,
            "text",
            19,
            'a'
        ),
        ["Endpoint parse: empty port"] = new(
            () => AprsIsEndpoint.Parse("rotate.aprs2.net:"),
            AprsValidationErrorCode.ServerPortInvalid,
            "text"
        ),
        ["Endpoint parse: port out of range"] = new(
            () => AprsIsEndpoint.Parse("rotate.aprs2.net:65536"),
            AprsValidationErrorCode.ServerPortOutOfRange,
            "text"
        ),
        ["Endpoint parse: port too long to be a number"] = new(
            () => AprsIsEndpoint.Parse("rotate.aprs2.net:99999999999"),
            AprsValidationErrorCode.ServerPortOutOfRange,
            "text"
        ),
    };

    public static TheoryData<string> Cases() => [.. _cases.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryRefusalCarriesACode(string name)
    {
        var expected = _cases[name];

        var ex = Assert.Throws<AprsValidationException>(expected.Act);

        Assert.Equal(expected.Code, ex.Code);
        Assert.Equal(expected.ParamName, ex.ParamName);
        Assert.Equal(expected.Index, ex.Index);
        Assert.Equal(expected.Character, ex.Character);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void EveryCodeIsThrownSomewhere()
    {
        var thrown = _cases.Values.Select(c => c.Code).ToHashSet();

        Assert.DoesNotContain(Enum.GetValues<AprsValidationErrorCode>(), code => !thrown.Contains(code));
    }

    [Fact]
    public void ExistingArgumentExceptionHandlersStillCatchTheErrors()
    {
        foreach (var (name, @case) in _cases)
        {
            try
            {
                @case.Act();
                Assert.Fail($"{name} did not throw");
            }
            catch (ArgumentException ex)
            {
                Assert.IsType<AprsValidationException>(ex);
            }
        }
    }

    [Fact]
    public void CodesAreUniqueDocumentedAndTracked()
    {
        var names = Enum.GetNames<AprsValidationErrorCode>();
        var values = Enum.GetValues<AprsValidationErrorCode>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());

        var assembly = typeof(AprsValidationErrorCode).Assembly;
        var docs = XDocument.Load(Path.ChangeExtension(assembly.Location, ".xml"));
        var documented = docs.Descendants("member")
            .Where(m => !string.IsNullOrWhiteSpace(m.Element("summary")?.Value))
            .Select(m => (string)m.Attribute("name")!)
            .ToHashSet();
        Assert.DoesNotContain(names, n => !documented.Contains($"F:AprsNet.Packets.AprsValidationErrorCode.{n}"));
    }

    [Fact]
    public void TheEnglishMessagesAreUnchanged()
    {
        Assert.StartsWith(
            "Callsign must be 1 to 9 letters, digits or '-'.",
            Assert.Throws<AprsValidationException>(() => Options(o => o.Callsign = "").Validate()).Message,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "Filter must not contain control characters.",
            Assert.Throws<AprsValidationException>(() => Options(o => o.Filter = "a\nb").Validate()).Message,
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "A callsign must be 1 to 9 printable ASCII characters",
            Assert.Throws<AprsValidationException>(() => Callsign.Parse("")).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void TryParseStillReturnsFalseWithoutThrowing()
    {
        Assert.False(AprsIsEndpoint.TryParse("[2001:db8::1", out _));
        Assert.False(AprsIsEndpoint.TryParse("rotate.aprs2.net:0", out _));
        Assert.True(AprsIsEndpoint.TryParse(" [2001:db8::1]:10152 ", out var endpoint));
        Assert.Equal("[2001:db8::1]:10152", endpoint.ToString());
    }

    [Fact]
    public void TheCodeVocabularyIsPartOfThePublicApi()
    {
        var project = Path.Combine(RepositoryRoot(), "src", "AprsNet.Packets");
        var lines = File.ReadAllLines(Path.Combine(project, "PublicAPI.Shipped.txt"))
            .Concat(File.ReadAllLines(Path.Combine(project, "PublicAPI.Unshipped.txt")))
            .ToHashSet();

        foreach (var code in Enum.GetValues<AprsValidationErrorCode>())
        {
            Assert.Contains(
                $"AprsNet.Packets.AprsValidationErrorCode.{code} = {(int)code} -> AprsNet.Packets.AprsValidationErrorCode",
                lines
            );
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AprsNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
