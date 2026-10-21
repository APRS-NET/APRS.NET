using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AprsNet.Packets;

namespace AprsNet.DeviceId.Tests;

public class AprsDeviceListTests
{
    private static readonly string _pinned = File.ReadAllText(
        Path.Combine(RepositoryRoot(), "tools", "deviceid", "tocalls.json")
    );

    // The pinned copy as the APRS Foundation publishes it: with its generation time.
    private static string Published(string generated = "2026-10-19T17:19:53Z")
    {
        var list = JsonNode.Parse(_pinned)!.AsObject();
        list["meta"] = new JsonObject { ["generation_time"] = generated };
        return list.ToJsonString();
    }

    private static string Minimal(string tocalls, string classes = """{"ht":{}}""", string extra = "") =>
        $$"""{"classes":{{classes}},"tocalls":{{tocalls}},"mice":{},"micelegacy":{}{{extra}}}""";

    [Fact]
    public void TheBuiltInListIsTheOneAprsDevicesUses()
    {
        var list = AprsDeviceList.BuiltIn;

        Assert.Same(list, AprsDeviceList.BuiltIn);
        Assert.Equal(AprsDevices.ListCommit, list.Commit);
        Assert.Equal(AprsDevices.ListGeneratedAt, list.GeneratedAt);
        Assert.Equal(AprsDevices.ListDate, DateOnly.FromDateTime(list.GeneratedAt!.Value.UtcDateTime));
        Assert.Same(AprsDevices.FromTocall("APDW17"), list.FromTocall("APDW17"));
    }

    [Fact]
    public void ThePublishedFileGivesTheSameLookupsAsTheBuiltInTable()
    {
        var loaded = AprsDeviceList.Parse(Published());
        var builtIn = AprsDeviceList.BuiltIn;
        var json = JsonNode.Parse(_pinned)!;

        Assert.Equal(builtIn.TocallCount, loaded.TocallCount);
        Assert.Equal(builtIn.GeneratedAt, loaded.GeneratedAt);
        Assert.Null(loaded.Commit); // the published file doesn't say

        // Every TOCALL, each pattern filled in two ways, and a few that aren't in the list.
        var tocalls = json["tocalls"]!
            .AsObject()
            .SelectMany(p =>
                new[]
                {
                    p.Key.Replace('n', '1').Replace('?', 'X').Replace("*", "", StringComparison.Ordinal),
                    p.Key.Replace('n', '9').Replace('?', '0').Replace("*", "ZZ", StringComparison.Ordinal),
                }
            )
            .Concat(["N0CALL", "APDW1", "APDWXYZ", ""]);
        Assert.All(tocalls, tocall => Assert.Equal(builtIn.FromTocall(tocall), loaded.FromTocall(tocall)));

        // Every Mic-E code, and every legacy Kenwood code with and without its suffix.
        var packets = json["mice"]!
            .AsObject()
            .Select(p => $"N0CALL-9>S32UVT:`(_fn\"Oj/`Hello{p.Key}")
            .Concat(
                json["micelegacy"]!
                    .AsObject()
                    .SelectMany(p =>
                        new[]
                        {
                            $"N0CALL-9>S32UVT:`(_fn\"Oj/{p.Value!["prefix"]}Hello{p.Value["suffix"]}",
                            $"N0CALL-9>S32UVT:`(_fn\"Oj/{p.Value["prefix"]}Hello",
                        }
                    )
            );
        Assert.All(
            packets,
            text =>
            {
                var packet = AprsPacket.Parse(text);
                Assert.NotNull(builtIn.Identify(packet));
                Assert.Equal(builtIn.Identify(packet), loaded.Identify(packet));
            }
        );
    }

    [Fact]
    public void TheCompactFormReadsTheSame()
    {
        var dense = JsonNode.Parse(Published())!.ToJsonString(); // no indentation, as tocalls.dense.json

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dense));
        Assert.True(AprsDeviceList.TryParse(stream, out var list, out var reason), reason);
        Assert.Equal("Kenwood TH-D74", list.FromTocall("APK004")!.ToString());
    }

    [Theory]
    [InlineData(
        """{"classes":{"ht":{},"robot":{}},"tocalls":{"APX":{}},"mice":{},"micelegacy":{}}""",
        "Unknown device class 'robot'"
    )]
    [InlineData(
        """{"classes":{},"tocalls":{"APX":{"class":"robot"}},"mice":{},"micelegacy":{}}""",
        "TOCALL 'APX': unknown device class 'robot'."
    )]
    [InlineData(
        """{"classes":{},"tocalls":{"APX":{"features":["teleport"]}},"mice":{},"micelegacy":{}}""",
        "unknown feature \"teleport\""
    )]
    [InlineData(
        """{"classes":{},"tocalls":{"AP*X":{}},"mice":{},"micelegacy":{}}""",
        "'*' is only supported at the end"
    )]
    [InlineData("""{"classes":{},"tocalls":{"APx1":{}},"mice":{},"micelegacy":{}}""", "a character other than")]
    [InlineData("""{"classes":{},"tocalls":{},"mice":{},"micelegacy":{}}""", "no TOCALLs")]
    [InlineData("""{"classes":{},"tocalls":{"APX":{}},"micelegacy":{}}""", "no 'mice' section")]
    [InlineData(
        """{"classes":{},"tocalls":{"APX":{}},"mice":{"X":{}},"micelegacy":{}}""",
        "Mic-E code 'X' is not 2 characters"
    )]
    [InlineData(
        """{"classes":{},"tocalls":{"APX":{}},"mice":{},"micelegacy":{"K":{"prefix":">>"}}}""",
        "prefix or suffix"
    )]
    [InlineData("""[1,2]""", "isn't a JSON object")]
    [InlineData("""<html>""", "isn't JSON")]
    public void AFileItCantReadIsRefusedWithTheReason(string json, string reason)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.False(AprsDeviceList.TryParse(stream, out var list, out var why));
        Assert.Null(list);
        Assert.Contains(reason, why, StringComparison.Ordinal);
        Assert.Contains(
            reason,
            Assert.Throws<InvalidDataException>(() => AprsDeviceList.Parse(json)).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ANewerListIsTheOneGeneratedLater()
    {
        var older = AprsDeviceList.Parse(
            Minimal("""{"APX":{}}""", extra: ""","meta":{"generation_time":"2026-10-01T00:00:00Z"}""")
        );
        var newer = AprsDeviceList.Parse(Published("2026-11-02T08:00:00Z"));
        var undated = AprsDeviceList.Parse(Minimal("""{"APX":{}}"""));

        Assert.True(newer.IsNewerThan(AprsDeviceList.BuiltIn));
        Assert.False(older.IsNewerThan(AprsDeviceList.BuiltIn));
        Assert.False(AprsDeviceList.BuiltIn.IsNewerThan(AprsDeviceList.BuiltIn));
        Assert.True(older.IsNewerThan(undated));
        Assert.False(undated.IsNewerThan(older));
        Assert.Null(undated.GeneratedAt);
    }

    [Fact]
    public async Task AnUnchangedListIsA304()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var source = new AprsDeviceListSource(new HttpClient(server));

        var fetch = await source.FetchAsync("\"c1934bf6\"", TestContext.Current.CancellationToken);

        Assert.Equal(AprsDeviceListFetchOutcome.Unchanged, fetch.Outcome);
        Assert.Null(fetch.List);
        Assert.Equal("\"c1934bf6\"", fetch.ETag);
        Assert.Equal(AprsDeviceListSource.DefaultUrl, server.Requests[0].RequestUri);
        Assert.Equal("\"c1934bf6\"", server.Requests[0].Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task ANewListComesWithItsETagAndTheFileAsPublished()
    {
        var published = Encoding.UTF8.GetBytes(Published("2026-11-02T08:00:00Z"));
        var server = new FakeServer(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(published) };
            response.Headers.ETag = new("\"d2a0\"");
            return response;
        });
        var mirror = new Uri("https://mirror.example.org/aprs/tocalls.dense.json");
        var source = new AprsDeviceListSource(new HttpClient(server), mirror);

        var fetch = await source.FetchAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AprsDeviceListFetchOutcome.Fetched, fetch.Outcome);
        Assert.Equal(mirror, server.Requests[0].RequestUri);
        Assert.Empty(server.Requests[0].Headers.IfNoneMatch);
        Assert.Equal("\"d2a0\"", fetch.ETag);
        Assert.Equal(published, fetch.Content.ToArray());
        Assert.True(fetch.List!.IsNewerThan(AprsDeviceList.BuiltIn));
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
    }

    [Fact]
    public async Task FailuresAreOutcomesNotExceptions()
    {
        static Task<AprsDeviceListFetch> Fetch(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
            new AprsDeviceListSource(new HttpClient(new FakeServer(answer))).FetchAsync(
                cancellationToken: TestContext.Current.CancellationToken
            );

        var missing = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var down = await Fetch(_ => throw new HttpRequestException("Connection refused"));
        var garbage = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>"),
        });

        Assert.Equal(AprsDeviceListFetchOutcome.UnexpectedAnswer, missing.Outcome);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(AprsDeviceListFetchOutcome.Unreachable, down.Outcome);
        Assert.Contains("Connection refused", down.Error, StringComparison.Ordinal);
        Assert.Equal(AprsDeviceListFetchOutcome.Unreadable, garbage.Outcome);
        Assert.Contains("isn't JSON", garbage.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingTheFetchThrows()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var source = new AprsDeviceListSource(
            new HttpClient(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK)))
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.FetchAsync(cancellationToken: cancel.Token)
        );
    }

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AprsNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("AprsNet.slnx not found above the tests.");
    }
}
