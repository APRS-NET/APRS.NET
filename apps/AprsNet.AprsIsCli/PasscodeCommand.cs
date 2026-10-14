using System.CommandLine;
using System.Globalization;
using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli;

/// <summary>The <c>passcode</c> command: print the APRS-IS passcode for a callsign.</summary>
internal sealed class PasscodeCommand(CliEnvironment environment)
{
    private readonly Argument<string> _callsign = new("callsign")
    {
        Description = "Callsign, with or without an SSID. The SSID and letter case do not change the passcode.",
    };

    public Command Build()
    {
        var command = new Command(
            "passcode",
            "Print the APRS-IS passcode for a callsign, for a verified login. Only for your own licensed callsign."
        );
        command.Arguments.Add(_callsign);

        command.SetAction(async parseResult =>
        {
            int passcode;
            try
            {
                passcode = AprsIsPasscode.Compute(parseResult.GetValue(_callsign)!.Trim());
            }
            catch (ArgumentException)
            {
                await environment.Error.WriteLineAsync(
                    "aprscli: the callsign must be 1 to 9 letters or digits, optionally followed by -SSID."
                );
                await environment.Error.WriteLineAsync("Try 'aprscli passcode --help'.");
                return ExitCodes.Usage;
            }

            // Only the number, so scripts can capture it: export APRS_PASSCODE=$(aprscli passcode N0CALL)
            await environment.Out.WriteLineAsync(passcode.ToString(CultureInfo.InvariantCulture));
            return ExitCodes.Success;
        });

        return command;
    }
}
