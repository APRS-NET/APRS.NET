using System.CommandLine;
using System.Net;
using System.Net.Http;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>health</c> command: ask a running <c>serve --http</c> whether it is serving, or check the file a
/// <c>listen --health-file</c> or <c>serve --health-file</c> keeps current, for a container <c>HEALTHCHECK</c> or a
/// monitoring script that has no HTTP client of its own.
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

    private readonly Option<string?> _file = new("--file")
    {
        Description =
            "Check the file a 'listen --health-file' or 'serve --health-file' keeps current instead of a URL: exits 0 "
            + "while it is younger than --max-age, 1 when it is older or missing.",
    };

    private readonly Option<string> _maxAge = new("--max-age")
    {
        Description =
            $"With --file, how old the file may be, for example 2m. Default: "
            + $"{DurationParser.Format(HealthFile.DefaultMaxAge)} (APRS-IS servers send a keepalive about every 20s).",
        DefaultValueFactory = _ => DurationParser.Format(HealthFile.DefaultMaxAge),
    };

    public Command Build()
    {
        var command = new Command(
            "health",
            "Check a running 'aprscli serve --http': prints its health report and exits 0 while any source is "
                + "connected, 1 when none is, 2 when it doesn't answer. With --file, check a health file instead."
        )
        {
            _url,
            _timeout,
            _file,
            _maxAge,
        };
        command.SetAction(CheckAsync);
        return command;
    }

    private async Task<int> CheckAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (parseResult.GetValue(_file) is { } file)
        {
            return await CheckFileAsync(parseResult, file);
        }

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

    /// <summary>
    /// <c>--file</c>: 0 while the file is younger than <c>--max-age</c>, otherwise 1 with the reason. Never 2, which
    /// Docker reserves.
    /// </summary>
    private async Task<int> CheckFileAsync(ParseResult parseResult, string path)
    {
        if (parseResult.GetResult(_url) is { Implicit: false })
        {
            await environment.Error.WriteLineAsync("aprscli: give either a URL or --file, not both.");
            await environment.Error.WriteLineAsync("Try 'aprscli health --help'.");
            return ExitCodes.Usage;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            await environment.Error.WriteLineAsync("aprscli: --file needs a file path.");
            return ExitCodes.Usage;
        }

        var maxAgeText = parseResult.GetValue(_maxAge)!;
        if (!DurationParser.TryParse(maxAgeText, out var maxAge))
        {
            await environment.Error.WriteLineAsync($"aprscli: --max-age '{maxAgeText}' is not valid. Use 90s or 2m.");
            return ExitCodes.Usage;
        }

        if (!HealthFile.TryRead(path, out var lastAlive, out var error))
        {
            await environment.Error.WriteLineAsync($"aprscli: unhealthy: {error}");
            return ExitCodes.Error;
        }

        var age = environment.TimeProvider.GetUtcNow() - lastAlive;
        var ago = age < TimeSpan.Zero ? "in the future" : $"{DurationParser.Format(Whole(age))} ago";
        var when = $"last alive {LineFormatter.Timestamp(lastAlive)} ({ago})";
        if (age > maxAge)
        {
            await environment.Error.WriteLineAsync(
                $"aprscli: unhealthy: {when}, older than --max-age {DurationParser.Format(maxAge)}."
            );
            return ExitCodes.Error;
        }

        await environment.Out.WriteLineAsync($"healthy: {when}");
        return ExitCodes.Success;
    }

    private static TimeSpan Whole(TimeSpan age) => TimeSpan.FromSeconds(Math.Floor(age.TotalSeconds));
}
