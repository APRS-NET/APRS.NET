using AprsNet.AprsIsTcpClient;
using Microsoft.Extensions.Options;

namespace AprsNet.Extensions.DependencyInjection;

/// <summary>Runs <see cref="AprsIsClientOptions.Validate"/> through the options system, so a bad setting is reported once.</summary>
internal sealed class AprsIsClientOptionsValidator : IValidateOptions<AprsIsClientOptions>
{
    public ValidateOptionsResult Validate(string? name, AprsIsClientOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
