using System.Threading.Channels;
using AprsNet.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AprsNet.AprsIsTcpClient;

/// <summary>
/// Connects to APRS-IS, logs in, exposes the incoming packet lines as an async stream, and sends packets on a verified
/// login (<see cref="SendAsync"/>).
/// A client is single-use: connect once, read until the stream ends, then dispose.
/// Lines are raw APRS text (<c>SRC&gt;DEST,PATH:body</c>); server comments (<c>#</c>) are counted but not delivered.
/// If the server sends nothing for <see cref="AprsIsClientOptions.IdleTimeout"/>, the stream ends with a
/// <see cref="TimeoutException"/>.
/// </summary>
public sealed class AprsIsClient : IAsyncDisposable
{
    private readonly AprsIsClientOptions _options;
    private readonly IAprsTransport _transport;
    private readonly ILogger _logger;
    private readonly Channel<string> _channel;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SendRateLimiter _sendLimiter;
    private readonly bool _ownsSendLimiter;
    private volatile bool _connectionEnded;
    private long _packetsSent;
    private Task? _receiveLoop;
    private int _started;
    private int _disposed;
    private long _packetsReceived;
    private long _serverCommentsReceived;

    /// <summary>Creates a client. Pass <paramref name="transport"/> to replace TCP, for example in tests.</summary>
    public AprsIsClient(AprsIsClientOptions options, ILogger? logger = null, IAprsTransport? transport = null)
        : this(options, logger, transport, sendLimiter: null) { }

    /// <summary>
    /// Creates a client that shares <paramref name="sendLimiter"/> with others, so an <see cref="AprsIsConnection"/>
    /// keeps one rate limit across reconnects. The client does not dispose a shared limiter.
    /// </summary>
    internal AprsIsClient(
        AprsIsClientOptions options,
        ILogger? logger,
        IAprsTransport? transport,
        SendRateLimiter? sendLimiter
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _ownsSendLimiter = sendLimiter is null;
        _sendLimiter =
            sendLimiter ?? new SendRateLimiter(options.SendBurst, options.SendInterval, options.TimeProvider);
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

    /// <summary>Whether the receive loop has ended: the server closed, the link failed or went silent.</summary>
    internal bool IsConnectionEnded => _connectionEnded;

    /// <summary>Packets written by <see cref="SendAsync"/> so far.</summary>
    public long PacketsSent => Interlocked.Read(ref _packetsSent);

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
    /// Throws if the connection failed with an error, or <see cref="TimeoutException"/> if the server went silent for
    /// longer than <see cref="AprsIsClientOptions.IdleTimeout"/>. Intended for a single consumer.
    /// </summary>
    public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Changes the server-side filter without reconnecting.</summary>
    public async ValueTask SetFilterAsync(string filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (AprsIsClientOptions.FilterError(filter, nameof(filter)) is { } error)
        {
            throw error;
        }

        if (Login is null)
        {
            throw new InvalidOperationException("Connect before changing the filter.");
        }

        await WriteLineAsync("#filter " + filter, cancellationToken).ConfigureAwait(false);
        _logger.FilterSet(filter);
    }

    /// <summary>
    /// Sends a packet, for example one built with <see cref="PacketAddress.Create(AprsPayload)"/>. The server adds its
    /// <c>qAC</c> q-construct and passes the packet on. Sends are spread out by <see cref="AprsIsClientOptions.SendBurst"/>
    /// and <see cref="AprsIsClientOptions.SendInterval"/>: when the burst is used up this waits for its turn.
    /// </summary>
    /// <remarks>
    /// APRS-IS has no acknowledgement for a sent packet: when this returns, the line has been written to the
    /// connection, not necessarily delivered. Messages are acknowledged by their recipient, see
    /// <see cref="AprsMessenger"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The client is not connected, the connection has ended, or the login is not verified: the server drops packets
    /// from an unverified login, so they are refused here rather than lost silently.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The packet has no header, holds a line break or NUL byte, is longer than an APRS-IS line (510 bytes), or
    /// already carries a q-construct (it was received from APRS-IS, and sending it again would loop it).
    /// </exception>
    public async ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CheckSendable(packet);
        var login = Login ?? throw new InvalidOperationException("Connect before sending.");
        if (login.Status != LoginStatus.Verified)
        {
            throw new InvalidOperationException(
                "The login is not verified, so the server would drop the packet. Log in with the passcode for "
                    + "the callsign (AprsIsPasscode.Compute)."
            );
        }

        await _sendLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_connectionEnded)
        {
            throw new InvalidOperationException("The connection has ended. Connect a new client to send.");
        }

        var line = packet.ToString(); // Latin-1, one character per byte: the transport writes the same bytes back
        await WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _packetsSent);
        ClientMetrics.PacketSent();
        _logger.PacketSent(line);
    }

    private static void CheckSendable(AprsPacket packet)
    {
        if (!packet.HasHeader)
        {
            throw new ArgumentException("The packet has no SOURCE>DESTINATION header.", nameof(packet));
        }

        var raw = packet.Raw.Span;
        if (raw.IndexOfAny("\r\n\0"u8) >= 0)
        {
            throw new ArgumentException("The packet holds a line break or NUL byte.", nameof(packet));
        }

        if (raw.Length > MaxLineLength)
        {
            throw new ArgumentException(
                $"The packet is {raw.Length} bytes; an APRS-IS line holds at most {MaxLineLength}.",
                nameof(packet)
            );
        }

        if (packet.Path is { } path && path.Any(entry => entry.Kind is PathEntryKind.QConstruct))
        {
            throw new ArgumentException(
                "The packet already carries an APRS-IS q-construct, so it came from APRS-IS; sending it again would "
                    + "loop it. Send packets as their originator builds them.",
                nameof(packet)
            );
        }
    }

    /// <summary>APRS-IS lines are at most 512 bytes including the CR LF.</summary>
    private const int MaxLineLength = 510;

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
        if (_ownsSendLimiter)
        {
            _sendLimiter.Dispose();
        }
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
        using var deadline = new CancellationTokenSource(_options.LoginTimeout, _options.TimeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

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
                string? line;
                if (_options.IdleTimeout == Timeout.InfiniteTimeSpan)
                {
                    line = await _transport.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // A fresh deadline per read, on the configured clock, so tests can drive it with a fake TimeProvider.
                    using var deadline = new CancellationTokenSource(_options.IdleTimeout, _options.TimeProvider);
                    using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                    try
                    {
                        line = await _transport.ReadLineAsync(read.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        ClientMetrics.IdleTimeout();
                        _logger.IdleTimeout(_options.IdleTimeout.TotalSeconds);
                        _channel.Writer.TryComplete(
                            new TimeoutException(
                                $"No data from the server for {_options.IdleTimeout.TotalSeconds:0.#}s."
                            )
                        );
                        return;
                    }
                }

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
        finally
        {
            _connectionEnded = true;
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
