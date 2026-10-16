using System.Net;
using AprsNet.Kiss;
using AprsNet.Packets;

namespace AprsNet.Docs.Tests.Snippets;

/// <summary>The code in docs/guides/tnc.md. Each region is one block of the guide; DocsSnippetTests keeps them equal.</summary>
internal static class TncGuide
{
    public static async Task ReadAsync(CancellationToken cancellationToken)
    {
        #region tnc-read
        // Dire Wolf on this computer, on its KISS TCP port (8001).
        await using var tnc = new KissTcpConnection("localhost");
        tnc.StateChanged += (_, e) =>
            Console.Error.WriteLine(
                e.State == KissConnectionState.Reconnecting
                    ? $"TNC lost ({e.Error?.Message ?? "closed"}); trying again in {e.RetryIn}"
                    : $"TNC {e.State}"
            );

        await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
        {
            Console.WriteLine($"{packet.Source}: {packet.Type} {packet}");
        }
        #endregion
    }

    public static async Task SendAsync(CancellationToken cancellationToken)
    {
        #region tnc-send
        await using var tnc = new KissTcpConnection("localhost");

        // Sending needs a live connection, and reading is what connects: read in the background.
        var connected = new TaskCompletionSource();
        tnc.StateChanged += (_, e) =>
        {
            if (e.State == KissConnectionState.Connected)
            {
                connected.TrySetResult();
            }
        };
        var reading = Task.Run(
            async () =>
            {
                await foreach (var heard in tnc.ReadPacketsAsync(cancellationToken))
                {
                    Console.WriteLine(heard);
                }
            },
            cancellationToken
        );
        await connected.Task.WaitAsync(cancellationToken);

        // Your callsign, APRS.NET's TOCALL and the usual two-hop path.
        var me = PacketAddress.Parse("N0CALL-9", "APNETL", ["WIDE1-1", "WIDE2-1"]);
        await tnc.SendAsync(me.Create(new StatusReport { Text = "On the air with APRS.NET" }), cancellationToken);
        #endregion

        await reading;
    }

    public static async Task LimitAsync(CancellationToken cancellationToken)
    {
        #region tnc-limit
        await using var tnc = new KissTcpConnection(
            new KissTcpOptions
            {
                Host = "192.168.1.20",
                Port = 8001,
                TncPort = 1, // the second radio on a two-port TNC
                TransmitLimit = new KissTransmitLimit(burst: 1, interval: TimeSpan.FromSeconds(30)),
            }
        );
        #endregion

        await foreach (var _ in tnc.ReadPacketsAsync(cancellationToken)) { }
    }

    public static async Task SerialAsync(CancellationToken cancellationToken)
    {
        #region tnc-serial
        // A hardware TNC in KISS mode on a USB serial port (AprsNet.Kiss.Serial).
        await using var tnc = new KissSerialConnection("/dev/ttyUSB0", 9600);

        await foreach (var packet in tnc.ReadPacketsAsync(cancellationToken))
        {
            Console.WriteLine(packet);
        }
        #endregion
    }

    public static async Task ShareAsync(CancellationToken cancellationToken)
    {
        #region tnc-share
        await using var tnc = new KissTcpConnection("localhost");
        await using var server = new KissTcpServer(new IPEndPoint(IPAddress.Loopback, 8101))
        {
            Transmit = tnc.SendFrameAsync, // every app's frames share the TNC's transmit limit
        };
        server.Start();

        await foreach (var frame in tnc.ReadFramesAsync(cancellationToken))
        {
            server.Publish(frame);
        }
        #endregion
    }
}
