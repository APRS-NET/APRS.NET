using System.Net;
using System.Net.Http.Headers;

namespace AprsNet.DeviceId;

/// <summary>
/// Fetches the published aprs-deviceid list over HTTP, asking only for a copy newer than the one the app has. Where the
/// copy is kept, how often to ask and which list to use stay with the app.
/// </summary>
/// <example>
/// <code>
/// var source = new AprsDeviceListSource(httpClient);
/// var fetch = await source.FetchAsync(savedETag, cancellationToken);
/// if (fetch.List is { } list &amp;&amp; list.IsNewerThan(current))
/// {
///     await File.WriteAllBytesAsync(path, fetch.Content.ToArray(), cancellationToken); // as published
///     (current, savedETag) = (list, fetch.ETag);
/// }
/// </code>
/// </example>
public sealed class AprsDeviceListSource
{
    private readonly HttpClient _http;

    /// <summary>
    /// Creates a source for the list at <paramref name="url"/>, or the APRS Foundation's (<see cref="DefaultUrl"/>).
    /// </summary>
    /// <param name="httpClient">The client to fetch with; the app owns it, and sets its timeout and user agent.</param>
    /// <param name="url">Where the list is published: a mirror, say.</param>
    public AprsDeviceListSource(HttpClient httpClient, Uri? url = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (url is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The address must be absolute.", nameof(url));
        }

        _http = httpClient;
        Url = url ?? DefaultUrl;
    }

    /// <summary>Where the APRS Foundation publishes the list: <c>https://aprs-deviceid.aprsfoundation.org/tocalls.pretty.json</c>.</summary>
    public static Uri DefaultUrl { get; } = new("https://aprs-deviceid.aprsfoundation.org/tocalls.pretty.json");

    /// <summary>Where this source fetches the list from.</summary>
    public Uri Url { get; }

    /// <summary>
    /// Fetches the list, unless it hasn't changed since the copy with <paramref name="etag"/>. Never throws for a
    /// failure: see <see cref="AprsDeviceListFetch.Outcome"/>.
    /// </summary>
    /// <param name="etag">The <see cref="AprsDeviceListFetch.ETag"/> of the copy the app has, if any.</param>
    /// <param name="cancellationToken">Cancels the fetch, which then throws <see cref="OperationCanceledException"/>.</param>
    public async Task<AprsDeviceListFetch> FetchAsync(
        string? etag = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (etag is not null)
        {
            if (!EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                throw new ArgumentException($"'{etag}' is not an entity tag.", nameof(etag));
            }

            request.Headers.IfNoneMatch.Add(tag);
        }

        HttpResponseMessage response;
        byte[] content;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
            when (e is HttpRequestException
                || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            )
        {
            // A timeout is the client's own cancellation, not the caller's.
            return new AprsDeviceListFetch(
                AprsDeviceListFetchOutcome.Unreachable,
                $"Couldn't reach {Url}: {e.Message}"
            );
        }

        using (response)
        {
            var status = response.StatusCode;
            if (status == HttpStatusCode.NotModified)
            {
                return new AprsDeviceListFetch(AprsDeviceListFetchOutcome.Unchanged, null)
                {
                    StatusCode = status,
                    ETag = etag,
                };
            }

            if (status != HttpStatusCode.OK)
            {
                return new AprsDeviceListFetch(
                    AprsDeviceListFetchOutcome.UnexpectedAnswer,
                    $"{Url} answered {(int)status} {response.ReasonPhrase}."
                )
                {
                    StatusCode = status,
                };
            }

            try
            {
                content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
                when (e is HttpRequestException or IOException
                    || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                )
            {
                return new AprsDeviceListFetch(
                    AprsDeviceListFetchOutcome.Unreachable,
                    $"The download from {Url} failed: {e.Message}"
                )
                {
                    StatusCode = status,
                };
            }

            var newTag = response.Headers.ETag?.ToString();
            if (!AprsDeviceList.TryParse(new MemoryStream(content, writable: false), out var list, out var reason))
            {
                return new AprsDeviceListFetch(AprsDeviceListFetchOutcome.Unreadable, reason) { StatusCode = status };
            }

            return new AprsDeviceListFetch(AprsDeviceListFetchOutcome.Fetched, null)
            {
                StatusCode = status,
                List = list,
                ETag = newTag,
                Content = content,
            };
        }
    }
}

/// <summary>What <see cref="AprsDeviceListSource.FetchAsync"/> found.</summary>
public sealed class AprsDeviceListFetch
{
    internal AprsDeviceListFetch(AprsDeviceListFetchOutcome outcome, string? error)
    {
        Outcome = outcome;
        Error = error;
    }

    /// <summary>What happened.</summary>
    public AprsDeviceListFetchOutcome Outcome { get; }

    /// <summary>The list, when <see cref="Outcome"/> is <see cref="AprsDeviceListFetchOutcome.Fetched"/>.</summary>
    public AprsDeviceList? List { get; init; }

    /// <summary>
    /// The file as published, when fetched: keep this to load the list again with <see cref="AprsDeviceList.Parse(Stream)"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// The copy's entity tag, to pass to the next <see cref="AprsDeviceListSource.FetchAsync"/>: the new one when
    /// fetched (if the server sent one), the one passed in when unchanged.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>The HTTP status the server answered with, when it answered.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>What went wrong, when the outcome is a failure.</summary>
    public string? Error { get; }
}

/// <summary>What a fetch of the device list found.</summary>
public enum AprsDeviceListFetchOutcome
{
    /// <summary>The server has a list, read into <see cref="AprsDeviceListFetch.List"/>.</summary>
    Fetched,

    /// <summary>The list hasn't changed since the copy with the entity tag given (HTTP 304).</summary>
    Unchanged,

    /// <summary>The server couldn't be reached, or the download broke off.</summary>
    Unreachable,

    /// <summary>The server answered, but not with the list (a 404, say).</summary>
    UnexpectedAnswer,

    /// <summary>The file isn't a list this version can read; <see cref="AprsDeviceListFetch.Error"/> says why.</summary>
    Unreadable,
}
