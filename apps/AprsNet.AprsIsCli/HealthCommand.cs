using System.CommandLine;
using System.Net;
using System.Net.Http;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>health</c> command: ask a running <c>serve --http</c> whether it is serving, for a container
/// <c>HEALTHCHECK</c> or a monitoring script that has no HTTP client of its own.
/// </summary>
internal sealed class HealthCommand(CliEnvironment environment)
{
    private const string DefaultUrl = "http://127.0.0.1:14590/health";

    private readonly Argument<string> _url = new("url")
    {
        Description = $"The health URL of aprscli serve --http. Default: {DefaultUrl}.",
        DefaultValueFactory = _ => DefaultUrl,
    };

    private readonly Option<string> _timeout = new("--timeout")
    {
        Description = "How long to wait for an answer, for example 5s. Default: 5s.",
        DefaultValueFactory = _ => "5s",
    };

    public Command Build()
    {
        var command = new Command(
            "health",
            "Check a running 'aprscli serve --http': prints its health report and exits 0 while any source is "
                + "connected, 1 when none is, 2 when it doesn't answer."
        )
        {
            _url,
            _timeout,
        };
        command.SetAction(CheckAsync);
        return command;
    }

    private async Task<int> CheckAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var text = parseResult.GetValue(_url)!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            await environment.Error.WriteLineAsync($"aprscli: '{text}' is not an http:// URL.");
            await environment.Error.WriteLineAsync("Try 'aprscli health --help'.");
            return ExitCodes.Usage;
        }

        var timeoutText = parseResult.GetValue(_timeout)!;
        if (!DurationParser.TryParse(timeoutText, out var timeout))
        {
            await environment.Error.WriteLineAsync($"aprscli: --timeout '{timeoutText}' is not valid. Use 5s or 1m.");
            return ExitCodes.Usage;
        }

        using var http = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            await environment.Out.WriteLineAsync(await response.Content.ReadAsStringAsync(cancellationToken));
            return response.IsSuccessStatusCode ? ExitCodes.Success
                : response.StatusCode == HttpStatusCode.ServiceUnavailable ? ExitCodes.Error
                : ExitCodes.ConnectionFailed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await environment.Error.WriteLineAsync($"aprscli: no answer from {url}: {ex.Message}");
            return ExitCodes.ConnectionFailed;
        }
    }
}
