using System.Net;
using System.Net.Sockets;

namespace AprsNet.Tests.Shared;

/// <summary>Ports for a test to hand to a server it starts, compiled into each test project that needs one.</summary>
internal static class TestPorts
{
    // Below every OS's range for the ports it picks itself (Linux 32768-60999; Windows and macOS 49152-65535). Port 0
    // would give one from that range, and once the probe closes it, another test's outgoing connection could take it
    // before the server under test binds it: the server then fails to start and the test waits for it.
    private const int First = 20000;
    private const int Last = 32000;

    private static readonly HashSet<int> _given = [];

    /// <summary>
    /// A loopback port that nothing listens on now, and that no other call in this test run has returned.
    /// </summary>
    public static int FreePort()
    {
        while (true)
        {
            var port = Random.Shared.Next(First, Last);
            lock (_given)
            {
                if (!_given.Add(port))
                {
                    continue;
                }
            }

            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return port;
            }
            catch (SocketException)
            {
                // Taken by something outside the tests: try another.
            }
        }
    }
}
