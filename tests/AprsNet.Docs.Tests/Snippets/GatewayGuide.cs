using System.Net;
using AprsNet.AprsIsTcpClient;
using AprsNet.Gateway;
using AprsNet.Kiss;

namespace AprsNet.Docs.Tests.Snippets;

/// <summary>The code in docs/guides/gateway.md. Each region is one block of the guide; DocsSnippetTests keeps them equal.</summary>
internal static class GatewayGuide
{
    public static async Task ServeAprsIsAsync(CancellationToken cancellationToken)
    {
        #region gateway-aprs-is
        // A local APRS-IS server on this computer's usual APRS-IS port.
        await using var server = new AprsIsServer(
            new AprsIsServerOptions { ServerName = "MYGATE", EndPoint = new IPEndPoint(IPAddress.Loopback, 14580) }
        );
        server.Start();

        // One read-only upstream connection with a broad filter: everything local clients may ask for.
        await using var aprsIs = new AprsIsConnection(
            new AprsIsClientOptions { Callsign = "N0CALL", Filter = "r/38.98/-94.67/300" }
        );
        await aprsIs.StartAsync(cancellationToken);

        // Serve until cancelled; the connection reconnects by itself.
        await using var feed = new GatewayFeed(server);
        await feed.AddSource("aprs-is", aprsIs.ReadPacketsAsync(cancellationToken));
        #endregion
    }

    public static async Task AddTncAsync(GatewayFeed feed, CancellationToken cancellationToken)
    {
        #region gateway-tnc
        await using var tnc = new KissTcpConnection("localhost");
        var hearing = feed.AddSource("tnc", tnc.ReadPacketsAsync(cancellationToken));

        // Later: what each source delivered, and how much of it the other source had already heard.
        foreach (var (source, counts) in feed.GetSourceCounts())
        {
            Console.WriteLine($"{source}: {counts.Packets} packets, {counts.Duplicates} duplicates");
        }
        #endregion

        await hearing;
    }

    public static AprsIsServerOptions History()
    {
        #region gateway-history
        var options = new AprsIsServerOptions
        {
            ServerName = "MYGATE",
            EndPoint = new IPEndPoint(IPAddress.Any, 14580), // serve the whole network
            History = TimeSpan.FromMinutes(30), // new clients get the last 30 minutes their filter passes
            Stations = new StationStore(), // last positions, for m/ and f/ filters
            MaxClients = 50,
        };
        #endregion
        return options;
    }
}
