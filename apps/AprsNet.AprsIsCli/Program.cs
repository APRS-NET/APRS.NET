using System.Text;
using AprsNet.AprsIsCli;
using Microsoft.Win32.SafeHandles;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // stop cleanly: close the capture file, exit 0
    // ReSharper disable once AccessToDisposedClosure
    cancellation.Cancel();
};

using var loggerFactory = Logging.CreateFactory();
using var meterProvider = Logging.CreateMeterProvider(Environment.GetEnvironmentVariable);
await using var stdout = OpenStandardOutput();
var environment = new CliEnvironment
{
    Out = stdout,
    OutIsTerminal = !Console.IsOutputRedirected,
    Error = Console.Error,
    In = new StreamReader(Console.OpenStandardInput(), Encoding.Latin1),
    InIsTerminal = !Console.IsInputRedirected,
    LoggerFactory = loggerFactory,
};

return await AprsCli.RunAsync(args, environment, cancellation.Token);

// .NET's console stream silently swallows broken-pipe errors on Unix, so `aprscli listen | head` would never stop.
// When stdout is a pipe or file, write through the raw file descriptor instead so a closed pipe raises IOException.
static TextWriter OpenStandardOutput()
{
    if (!Console.IsOutputRedirected || OperatingSystem.IsWindows())
    {
        return Console.Out;
    }

    var stream = new FileStream(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write, bufferSize: 1);
    return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
}
