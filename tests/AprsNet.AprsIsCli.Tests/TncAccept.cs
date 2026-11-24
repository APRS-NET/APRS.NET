using System.Net.Sockets;

namespace AprsNet.AprsIsCli.Tests;

internal static class TncAccept
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits for the command in <paramref name="run"/> to connect to this fake TNC. A command that ends first, or
    /// doesn't connect within 10 s, fails the test at once with what it printed, rather than leaving it waiting for a
    /// connection that will never come.
    /// </summary>
    public static async Task<TcpClient> AcceptFromAsync(
        this TcpListener tnc,
        Task<CliResult> run,
        CancellationToken cancellationToken
    )
    {
        var accept = tnc.AcceptTcpClientAsync(cancellationToken).AsTask();
        var timeout = Task.Delay(_timeout, cancellationToken);
        var first = await Task.WhenAny(accept, run, timeout);
        if (first == accept)
        {
            return await accept;
        }

        if (first == run)
        {
            var result = await run;
            Assert.Fail(
                $"The command ended with exit code {result.ExitCode} before connecting to the TNC: {result.Error}"
            );
        }

        Assert.Fail($"The command did not connect to the TNC within {_timeout.TotalSeconds} s.");
        return null!; // Assert.Fail throws
    }
}
