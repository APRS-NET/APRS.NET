using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// Connects to APRS-IS, logs in, and exposes the incoming packet lines as an async stream.
/// A client is single-use: connect once, read until the stream ends, then dispose.
/// Lines are raw APRS text (<c>SRC&gt;DEST,PATH:body</c>); server comments (<c>#</c>) are counted but not delivered.
/// </summary>
public sealed class AprsIsClient : IAsyncDisposable
{
    private readonly AprsIsClientOptions _options;
    private readonly IAprsTransport _transport;
    private readonly ILogger _logger;
    private readonly Channel<string> _channel;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _receiveLoop;
    private int _started;
    private int _disposed;
    private long _packetsReceived;
    private long _serverCommentsReceived;

    /// <summary>Creates a client. Pass <paramref name="transport"/> to replace TCP, for example in tests.</summary>
    public AprsIsClient(AprsIsClientOptions options, ILogger? logger = null, IAprsTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _logger = logger ?? NullLogger.Instance;
        _transport =
            transport ?? new TcpAprsTransport(options.Endpoint, options.ConnectTimeout, options.MaxLineLength, _logger);

        var channelOptions = new BoundedChannelOptions(options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode =
                options.OverflowPolicy == OverflowPolicy.Block
                    ? BoundedChannelFullMode.Wait
                    : BoundedChannelFullMode.DropOldest,
        };
        _channel = Channel.CreateBounded<string>(
            channelOptions,
            _ =>
            {
                ClientMetrics.QueueFull();
                _logger.QueueFull();
            }
        );
    }

    /// <summary>The server's login response, once <see cref="ConnectAsync"/> has completed.</summary>
    public LoginResult? Login { get; private set; }

    /// <summary>Packet lines read from the server so far.</summary>
    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

    /// <summary>Server comment lines (<c>#</c>) read so far.</summary>
    public long ServerCommentsReceived => Interlocked.Read(ref _serverCommentsReceived);

    /// <summary>Connects, sends the login line, waits for the server's response, and starts receiving.</summary>
    /// <exception cref="LoginRejectedException">The server refused the login, for example <c># Login by user not allowed</c>.</exception>
    /// <exception cref="LoginException">The server closed the connection, did not answer in time, or answered unreadably.</exception>
    public async Task<LoginResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException(
                "This client has already been connected. Create a new client to reconnect."
            );
        }

        try
        {
            _logger.Connecting(
                _options.Endpoint,
                _options.Callsign,
                _options.IsReadOnly ? "read-only" : "verified",
                _options.Filter ?? "(none)"
            );
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await WriteLineAsync(BuildLoginLine(), cancellationToken).ConfigureAwait(false);
            var result = await ReadLoginResponseAsync(cancellationToken).ConfigureAwait(false);

            Login = result;
            ClientMetrics.Attempt("connected");
            ClientMetrics.Login(result.Status);
            _logger.LoggedIn(result.Status, result.Callsign, result.Server);
            if (result.Status == LoginStatus.Unverified && !_options.IsReadOnly)
            {
                _logger.PasscodeNotAccepted();
            }

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_shutdown.Token), CancellationToken.None);
            return result;
        }
        catch (Exception ex)
        {
            if (ex is LoginRejectedException rejected)
            {
                ClientMetrics.LoginRejected();
                _logger.LoginRejected(_options.Callsign, rejected.ServerMessage);
            }

            ClientMetrics.Attempt(
                ex is TimeoutException or LoginException { InnerException: TimeoutException } ? "timeout" : "failed"
            );
            throw;
        }
    }

    /// <summary>
    /// Yields packet lines until the server closes the connection or the client is disposed.
    /// Throws if the connection failed with an error. Intended for a single consumer.
    /// </summary>
    public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Changes the server-side filter without reconnecting.</summary>
    public async ValueTask SetFilterAsync(string filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (AprsIsClientOptions.ContainsLineBreakOrControl(filter))
        {
            throw new ArgumentException("Filter must not contain control characters.", nameof(filter));
        }

        if (Login is null)
        {
            throw new InvalidOperationException("Connect before changing the filter.");
        }

        await WriteLineAsync("#filter " + filter, cancellationToken).ConfigureAwait(false);
        _logger.FilterSet(filter);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_receiveLoop is not null)
        {
            await _receiveLoop.ConfigureAwait(false); // the loop catches its own exceptions
        }

        _channel.Writer.TryComplete();
        await _transport.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _shutdown.Dispose();
    }

    private string BuildLoginLine()
    {
        var line =
            $"user {_options.Callsign} pass {_options.Passcode} vers {_options.SoftwareName} {_options.SoftwareVersion}";
        return _options.Filter is { Length: > 0 } filter ? $"{line} filter {filter}" : line;
    }

    private async Task<LoginResult> ReadLoginResponseAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.LoginTimeout);

        try
        {
            while (true)
            {
                var line =
                    await _transport.ReadLineAsync(timeout.Token).ConfigureAwait(false)
                    ?? throw new LoginException("The server closed the connection before answering the login.");

                if (LoginRejectedException.IsRejection(line))
                {
                    throw LoginRejectedException.FromLine(line);
                }

                if (line.StartsWith("# logresp", StringComparison.Ordinal))
                {
                    return LoginResult.TryParse(line) ?? throw new LoginException($"Unreadable login response: {line}");
                }

                _logger.ServerLine(line);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LoginException(
                $"No login response within {_options.LoginTimeout.TotalSeconds:0.#}s.",
                new TimeoutException()
            );
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await _transport.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    _logger.ServerClosed();
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '#')
                {
                    Interlocked.Increment(ref _serverCommentsReceived);
                    ClientMetrics.ServerComment();
                    _logger.ServerLine(line);
                    continue;
                }

                Interlocked.Increment(ref _packetsReceived);
                ClientMetrics.Packet();
                await _channel.Writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            }

            _channel.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _channel.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger.ReceiveFailed(ex);
            _channel.Writer.TryComplete(ex);
        }
    }

    private async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
