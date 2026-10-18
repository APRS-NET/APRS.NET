using AprsNet.AprsIsTcpClient;

namespace AprsNet.AprsIsCli;

/// <summary>
/// Reports an <see cref="AprsIsConnection"/>'s drops and reconnects on standard error, and closes it when the command
/// should stop instead of reconnecting: always without <c>--reconnect</c>, and on a rejected login or a KISS TNC either
/// way.
/// </summary>
/// <remarks>
/// Stopping disposes the connection rather than cancelling the reader, so lines already received are still printed
/// before the line stream ends.
/// </remarks>
internal sealed class ConnectionReporter : IDisposable
{
    private readonly AprsIsConnection _connection;
    private readonly TextWriter _error;
    private readonly AprsIsEndpoint _endpoint;
    private readonly bool _reconnect;
    private readonly CancellationTokenSource _stopped = new();
    private AprsIsConnectionState _previous = AprsIsConnectionState.NotStarted;

    public ConnectionReporter(AprsIsConnection connection, TextWriter error, AprsIsEndpoint endpoint, bool reconnect)
    {
        _connection = connection;
        _error = error;
        _endpoint = endpoint;
        _reconnect = reconnect;
        connection.StateChanged += OnStateChanged;
    }

    /// <summary>The exit code once the connection was closed because of a failure; null while it is still up.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Cancelled when the connection was closed because of a failure.</summary>
    public CancellationToken Stopped => _stopped.Token;

    /// <summary>Completes once a connection closed because of a failure has finished closing.</summary>
    public Task Closing { get; private set; } = Task.CompletedTask;

    public void Dispose()
    {
        _connection.StateChanged -= OnStateChanged;
        _stopped.Dispose();
    }

    private void OnStateChanged(object? sender, AprsIsConnectionStateChangedEventArgs e)
    {
        var previous = _previous;
        _previous = e.State;
        if (e.State != AprsIsConnectionState.Reconnecting)
        {
            return;
        }

        // A failed attempt says why it could not connect; a dropped connection says how it ended.
        _error.WriteLine(previous == AprsIsConnectionState.Connecting ? AttemptFailed(e.Error) : Lost(e.Error));

        int? exitCode =
            e.Error is LoginRejectedException or KissTncLoginException ? ExitCodes.LoginFailed
            : _reconnect ? null
            : ExitCodes.ConnectionFailed;
        if (exitCode is null)
        {
            _error.WriteLine($"aprscli: reconnecting to {_endpoint} in {e.RetryIn!.Value.TotalSeconds:0.#}s.");
            return;
        }

        ExitCode = exitCode;
        _stopped.Cancel();

        // Not awaited here: closing waits for the connection's own task, which is the one raising this event.
        Closing = Task.Run(async () => await _connection.DisposeAsync());
    }

    private string Lost(Exception? error) =>
        error is null ? $"aprscli: {_endpoint} closed the connection." : $"aprscli: connection lost: {error.Message}";

    private string AttemptFailed(Exception? error) =>
        error switch
        {
            LoginRejectedException rejected => $"aprscli: login rejected by {_endpoint}: {rejected.ServerMessage}",
            KissTncLoginException => KissTnc(_endpoint),
            LoginException login => $"aprscli: login failed on {_endpoint}: {login.Message}",
            _ => $"aprscli: could not connect to {_endpoint}: {error?.Message}",
        };

    /// <summary>What to say when an APRS-IS endpoint turns out to be a KISS TNC, such as Dire Wolf's port 8001.</summary>
    public static string KissTnc(AprsIsEndpoint endpoint) =>
        $"aprscli: {endpoint} is a KISS TNC, not an APRS-IS server. To hear it, run: aprscli tnc listen --tnc {endpoint}";
}
