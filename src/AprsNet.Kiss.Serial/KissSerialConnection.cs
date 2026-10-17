using System.IO.Ports;

namespace AprsNet.Kiss;

/// <summary>
/// A lasting connection to a KISS TNC on a serial port, such as a USB TNC (the MFJ TNC-X or the NinoTNC) or a
/// Bluetooth serial link. It opens the port when a read starts and, as <see cref="KissConnection"/> describes, opens it
/// again when it fails or goes away, so unplugging the TNC and plugging it back in only pauses the reads.
/// </summary>
/// <remarks>
/// The port is opened at 8 data bits, no parity, one stop bit and no handshaking, as KISS TNCs expect. A USB TNC that
/// comes back under another name (<c>/dev/ttyUSB1</c> instead of <c>/dev/ttyUSB0</c>) isn't found; on Linux, name it
/// by its stable link under <c>/dev/serial/by-id/</c>.
/// </remarks>
public sealed class KissSerialConnection : KissConnection
{
    /// <summary>
    /// Creates a connection to the TNC on <paramref name="portName"/> with the other settings at their defaults;
    /// nothing opens until a read starts.
    /// </summary>
    /// <exception cref="ArgumentException">The port name is empty or the baud rate is not positive.</exception>
    public KissSerialConnection(string portName, int baudRate = KissSerialOptions.DefaultBaudRate)
        : this(new KissSerialOptions { PortName = portName, BaudRate = baudRate }) { }

    /// <summary>Creates a connection with <paramref name="options"/>; nothing opens until a read starts.</summary>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is unusable.</exception>
    public KissSerialConnection(KissSerialOptions options)
        : this(OpenSerial(Validated(options)), options) { }

    internal KissSerialConnection(Func<CancellationToken, Task<Stream>> open, KissSerialOptions options)
        : base(
            open,
            Validated(options).PortName,
            options.TncPort,
            options.ReconnectDelay,
            options.MaxReconnectDelay,
            options.TimeProvider,
            options.CreateTransmitLimit()
        )
    {
        PortName = options.PortName;
    }

    /// <summary>The serial port the TNC is on.</summary>
    public string PortName { get; }

    /// <summary>
    /// The serial ports present now, sorted, for a settings picker: <c>COM</c> ports on Windows, and <c>/dev/tty*</c>
    /// serial devices on Linux and macOS, including those of plugged-in USB TNCs.
    /// </summary>
    public static IReadOnlyList<string> GetPortNames() =>
        SerialPort.GetPortNames().Distinct().Order(StringComparer.Ordinal).ToArray();

    private static KissSerialOptions Validated(KissSerialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    private static Func<CancellationToken, Task<Stream>> OpenSerial(KissSerialOptions options)
    {
        var (portName, baudRate) = (options.PortName, options.BaudRate);
        return token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(OpenPort(portName, baudRate));
        };
    }

    /// <summary>
    /// Opens a serial port at 8N1 with no handshaking, as a stream that reports the device going away as an
    /// <see cref="IOException"/>. The CLI's one-shot serial link opens ports this way too.
    /// </summary>
    /// <exception cref="IOException">The port can't be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The port is held by another program, or may not be opened.</exception>
    internal static Stream OpenPort(string portName, int baudRate)
    {
        // DTR and RTS raised, as a terminal program leaves them: some radios (the Kenwood TH-D72A among them)
        // hold their output while RTS is low.
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
        };
        try
        {
            port.Open();
            return new SerialPortStream(port.BaseStream, port);
        }
        catch (ArgumentException e)
        {
            // Windows refuses a name that isn't a COM port this way; treat it like any port that can't be opened.
            port.Dispose();
            throw new IOException($"Cannot open the serial port {portName}: {e.Message}", e);
        }
        catch (UnauthorizedAccessException e) when (portName.StartsWith('/') && !File.Exists(portName))
        {
            // .NET on Linux reports a device that isn't there as access denied.
            port.Dispose();
            throw new IOException($"The serial port {portName} is not present; is the TNC plugged in?", e);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A serial port's stream that closes the port with it. A serial read does not always notice cancellation, so
    /// cancelling a read closes the port. When the device goes away, .NET closes the port itself and the next read or
    /// write throws <see cref="ObjectDisposedException"/> ("The port is closed") or
    /// <see cref="InvalidOperationException"/>; those become an <see cref="IOException"/>, a lost link to reconnect.
    /// </summary>
    internal sealed class SerialPortStream(Stream stream, IDisposable port) : Stream
    {
        private readonly Stream _stream = stream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return _stream.Read(buffer, offset, count);
            }
            catch (Exception e) when (IsClosedPort(e))
            {
                throw Lost(e);
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await using var closing = cancellationToken.Register(port.Dispose).ConfigureAwait(false);
            try
            {
                return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (cancellationToken.IsCancellationRequested && e is not OperationCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception e) when (IsClosedPort(e))
            {
                throw Lost(e);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            try
            {
                _stream.Write(buffer, offset, count);
            }
            catch (Exception e) when (IsClosedPort(e))
            {
                throw Lost(e);
            }
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested && IsClosedPort(e))
            {
                throw Lost(e);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                port.Dispose();
            }

            base.Dispose(disposing);
        }

        private static bool IsClosedPort(Exception e) => e is ObjectDisposedException or InvalidOperationException;

        private static IOException Lost(Exception e) =>
            new($"The serial port closed; the TNC was probably unplugged. ({e.Message})", e);
    }
}
