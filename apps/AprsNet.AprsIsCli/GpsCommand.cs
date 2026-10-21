using System.CommandLine;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using AprsNet.Gps;
using AprsNet.Packets;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The <c>gps</c> command: read a GPS through gpsd, or its NMEA sentences over TCP or from a serial port, and print a line for each fix, with
/// the satellites or the lines as read when asked. A check of a GPS setup, and the live proof of the GPS capability.
/// </summary>
internal sealed class GpsCommand(CliEnvironment environment)
{
    private const int DefaultBaudRate = 4800;

    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(1);

    private readonly Option<string?> _gpsd = new("--gpsd")
    {
        Description = $"Read gpsd at host[:port] (default localhost:{GpsdOptions.DefaultPort}).",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private readonly Option<string?> _nmea = new("--nmea")
    {
        Description =
            "Read NMEA sentences over TCP from host:port, as a GPS-to-network bridge or gpsd's NMEA port sends them.",
    };

    private readonly Option<string?> _serial = new("--serial")
    {
        Description =
            $"Read NMEA sentences from a GPS on a serial port, as PORT[@BAUD], such as /dev/ttyACM0 or COM4@9600 "
            + $"(default {DefaultBaudRate} baud, NMEA's standard speed).",
    };

    private readonly Option<string?> _device = new("--device")
    {
        Description = "With --gpsd: the GPS to read when gpsd has several, such as /dev/ttyACM0.",
    };

    private readonly Option<int?> _count = new("--count", "-n") { Description = "Stop after this many fixes." };

    private readonly Option<string?> _duration = new("--duration", "-d")
    {
        Description = "Stop after this long, for example 90s, 10m or 2h.",
    };

    private readonly Option<bool> _satellites = new("--satellites", "-s")
    {
        Description = "After a fix, list the satellites in view when they have changed.",
    };

    private readonly Option<bool> _raw = new("--raw")
    {
        Description = "Print the lines as read (gpsd's JSON or the NMEA sentences) instead of the fixes.",
    };

    public Command Build()
    {
        var command = new Command(
            "gps",
            "Read a GPS through gpsd, as NMEA over TCP or on a serial port, and print its fixes: time, fix, position, altitude, speed, "
                + "course, satellites, HDOP and error. It keeps trying when the GPS source goes away (waiting 5s, "
                + "doubling to 60s) until --count, --duration or Ctrl+C."
        )
        {
            _gpsd,
            _nmea,
            _serial,
            _device,
            _count,
            _duration,
            _satellites,
            _raw,
        };
        command.SetAction(RunAsync);
        return command;
    }

    private async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var gpsd = parseResult.GetResult(_gpsd) is not null;
        var nmea = parseResult.GetValue(_nmea);
        var serial = parseResult.GetValue(_serial);
        if ((gpsd ? 1 : 0) + (nmea is null ? 0 : 1) + (serial is null ? 0 : 1) != 1)
        {
            return await UsageAsync(
                "Give one GPS source: --gpsd [host[:port]], --nmea host:port or --serial PORT[@BAUD]."
            );
        }

        if (parseResult.GetValue(_device) is not null && !gpsd)
        {
            return await UsageAsync("--device only applies with --gpsd.");
        }

        string host = "";
        int port = 0;
        var baudRate = DefaultBaudRate;
        if (serial is not null)
        {
            var at = serial.LastIndexOf('@');
            if (
                at == 0
                || (
                    at > 0
                    && !int.TryParse(
                        serial.AsSpan(at + 1),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out baudRate
                    )
                )
                || baudRate <= 0
                || string.IsNullOrWhiteSpace(serial)
            )
            {
                return await UsageAsync(
                    $"--serial '{serial}' is not valid. Use PORT[@BAUD], such as /dev/ttyACM0@9600."
                );
            }

            serial = at > 0 ? serial[..at] : serial;
        }
        else if (gpsd)
        {
            if (!TryParseHost(parseResult.GetValue(_gpsd) ?? "localhost", GpsdOptions.DefaultPort, out host, out port))
            {
                return await UsageAsync($"--gpsd '{parseResult.GetValue(_gpsd)}' is not valid. Use host[:port].");
            }
        }
        else if (nmea is not null && !TryParseHost(nmea, null, out host, out port))
        {
            return await UsageAsync($"--nmea '{nmea}' is not valid. Use host:port, such as 192.168.1.20:10110.");
        }

        if (parseResult.GetValue(_count) is <= 0)
        {
            return await UsageAsync("--count must be at least 1.");
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (parseResult.GetValue(_duration) is { } durationText)
        {
            if (!DurationParser.TryParse(durationText, out var duration))
            {
                return await UsageAsync($"--duration '{durationText}' is not valid. Use a duration such as 10m.");
            }

            run.CancelAfter(duration);
        }

        var printer = new FixPrinter(
            environment.Out,
            parseResult.GetValue(_satellites),
            parseResult.GetValue(_raw),
            parseResult.GetValue(_count),
            run
        );
        try
        {
            if (gpsd)
            {
                await ReadGpsdAsync(host, port, parseResult.GetValue(_device), printer, run.Token);
            }
            else if (serial is not null)
            {
                var (name, speed) = (serial, baudRate);
                await ReadNmeaLinesAsync(
                    name,
                    "the port closed",
                    _ => Task.FromResult(environment.OpenGpsPort(name, speed)),
                    printer,
                    run.Token
                );
            }
            else
            {
                await ReadNmeaAsync(host, port, printer, run.Token);
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // --count, --duration or Ctrl+C.
        }

        return ExitCodes.Success;
    }

    private async Task ReadGpsdAsync(
        string host,
        int port,
        string? device,
        FixPrinter printer,
        CancellationToken cancellationToken
    )
    {
        await using var gps = new GpsdConnection(
            new GpsdOptions
            {
                Host = host,
                Port = port,
                Device = device,
                ReconnectDelay = environment.ReconnectDelay,
                MaxReconnectDelay = MaxReconnectDelay,
                TimeProvider = environment.TimeProvider,
            }
        );
        var error = environment.Error;
        gps.StateChanged += (_, e) =>
        {
            switch (e.State)
            {
                case GpsdConnectionState.Connected:
                    error.WriteLine($"aprscli: connected to gpsd at {gps.Server}");
                    break;
                case GpsdConnectionState.Reconnecting:
                    var why = e.Error is null ? "it closed the connection" : e.Error.Message;
                    error.WriteLine(
                        $"aprscli: lost gpsd at {gps.Server} ({why}); trying again in {DurationParser.Format(e.RetryIn!.Value)}"
                    );
                    break;
            }
        };
        gps.LineRead += (_, e) => printer.Line(e.Line);
        string? lastError = null;
        await foreach (var fix in gps.ReadFixesAsync(cancellationToken))
        {
            if (gps.LastError is { } said && said != lastError)
            {
                lastError = said;
                await error.WriteLineAsync($"aprscli: gpsd says: {said}");
            }

            printer.Fix(fix, gps.Quality, gps.Satellites);
        }
    }

    private async Task ReadNmeaAsync(string host, int port, FixPrinter printer, CancellationToken cancellationToken)
    {
        var where = $"{host}:{port}";
        await ReadNmeaLinesAsync(
            where,
            "it closed the connection",
            async token =>
            {
                var client = new TcpClient { NoDelay = true };
                try
                {
                    await client.ConnectAsync(host, port, token);
                    return new OwnedStream(client.GetStream(), client);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            },
            printer,
            cancellationToken
        );
    }

    /// <summary>
    /// Reads NMEA sentences from the streams <paramref name="open"/> gives, opening it again whenever it ends or fails,
    /// and prints a fix for each second the GPS reports.
    /// </summary>
    private async Task ReadNmeaLinesAsync(
        string where,
        string ended,
        Func<CancellationToken, Task<Stream>> open,
        FixPrinter printer,
        CancellationToken cancellationToken
    )
    {
        var gps = new NmeaFixTracker();
        var delay = environment.ReconnectDelay;
        while (true)
        {
            string why;
            try
            {
                await using var stream = await open(cancellationToken);
                await environment.Error.WriteLineAsync($"aprscli: reading NMEA from {where}");
                delay = environment.ReconnectDelay;
                gps.Reset();
                using var reader = new StreamReader(stream, Encoding.Latin1);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    printer.Line(line);
                    if (!NmeaSentence.TryParse(line, out var sentence))
                    {
                        continue;
                    }

                    // A sentence from a new second: the one before is complete.
                    var before = gps.Fix;
                    gps.Update(sentence);
                    if (before.UtcTime is { } time && gps.Fix.UtcTime != time)
                    {
                        printer.Fix(before, gps.Quality, gps.Satellites);
                    }
                }

                why = ended;
            }
            catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
            {
                why = ex.Message;
            }

            await environment.Error.WriteLineAsync(
                $"aprscli: lost {where} ({why}); trying again in {DurationParser.Format(delay)}"
            );
            await Task.Delay(delay, environment.TimeProvider, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
        }
    }

    /// <summary>host, host:port or [v6]:port; the port is required when there is no default.</summary>
    private static bool TryParseHost(string text, int? defaultPort, out string host, out int port)
    {
        (host, port) = ("", 0);
        text = text.Trim();
        string? portText = null;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                {
                    return false;
                }

                portText = rest[1..];
            }
        }
        else if (text.Count(c => c == ':') == 1)
        {
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            (host, portText) = (text[..colon], text[(colon + 1)..]);
        }
        else
        {
            host = text;
        }

        if (portText is null)
        {
            port = defaultPort ?? 0;
            return host.Length > 0 && defaultPort is not null;
        }

        return host.Length > 0
            && int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65535;
    }

    private async Task<int> UsageAsync(string message)
    {
        await environment.Error.WriteLineAsync($"aprscli: {message}");
        await environment.Error.WriteLineAsync("Try 'aprscli gps --help'.");
        return ExitCodes.Usage;
    }

    /// <summary>A stream that also disposes what it was opened from, such as its <see cref="TcpClient"/>.</summary>
    private sealed class OwnedStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Prints fixes, satellites or raw lines for <c>aprscli gps</c>, and ends the run after <c>--count</c> fixes.</summary>
internal sealed class FixPrinter(TextWriter output, bool satellites, bool raw, int? count, CancellationTokenSource run)
{
    private IReadOnlyList<GpsSatellite>? _shown;
    private int _fixes;

    public void Line(string line)
    {
        if (raw)
        {
            output.WriteLine(line);
        }
    }

    public void Fix(GpsFix fix, GpsQuality quality, IReadOnlyList<GpsSatellite> inView)
    {
        if (!raw)
        {
            output.WriteLine(Format(fix, quality));
            if (satellites && !ReferenceEquals(inView, _shown))
            {
                _shown = inView;
                foreach (var satellite in inView)
                {
                    output.WriteLine(Format(satellite));
                }
            }
        }

        if (++_fixes == count)
        {
            run.Cancel();
        }
    }

    /// <summary>
    /// <c>2026-10-20 14:00:06Z 3D 49.05833 -72.02736 105.3 m 48.7 kn 90° 9/12 sats HDOP 0.92 ±3.1 m</c>: what the fix
    /// says, leaving out what it doesn't.
    /// </summary>
    public static string Format(GpsFix fix, GpsQuality quality)
    {
        var parts = new List<string>
        {
            fix.Time is { } time ? time.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : fix.UtcTime is { } clock ? clock.ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : "--:--:--Z",
            fix.Mode switch
            {
                GpsFixMode.NoFix => "no fix",
                GpsFixMode.Fix => "fix",
                GpsFixMode.Fix2D => "2D",
                GpsFixMode.Fix3D => "3D",
                _ => "?",
            },
        };
        if (fix.Position is { } position)
        {
            parts.Add(Invariant($"{position.Latitude:0.00000} {position.Longitude:0.00000}"));
        }

        if (fix.AltitudeMeters is { } altitude)
        {
            parts.Add(Invariant($"{altitude:0.0} m"));
        }

        if (fix.SpeedKnots is { } speed)
        {
            parts.Add(Invariant($"{speed:0.0} kn"));
        }

        if (fix.Course is { } course)
        {
            parts.Add(Invariant($"{course:0}°"));
        }

        if ((quality.SatellitesUsed ?? fix.Satellites) is { } used)
        {
            parts.Add(
                quality.SatellitesInView is { } inView ? Invariant($"{used}/{inView} sats") : Invariant($"{used} sats")
            );
        }

        if ((quality.Hdop ?? fix.Hdop) is { } hdop)
        {
            parts.Add(Invariant($"HDOP {hdop:0.0#}"));
        }

        if (quality.HorizontalErrorMeters is { } error)
        {
            parts.Add(Invariant($"±{error:0.0} m"));
        }

        return string.Join(' ', parts);
    }

    /// <summary><c>  GPS       5 sig 1 el 40 az  83 46 dB-Hz used</c>.</summary>
    public static string Format(GpsSatellite satellite)
    {
        var line = new StringBuilder(Invariant($"  {Name(satellite.System), -8}{satellite.Id, 4}"));
        if (satellite.SignalId is { } signal)
        {
            line.Append(Invariant($" sig {signal}"));
        }

        line.Append(satellite.ElevationDegrees is { } el ? Invariant($" el {el, 2:0}") : " el --");
        line.Append(satellite.AzimuthDegrees is { } az ? Invariant($" az {az, 3:0}") : " az ---");
        line.Append(satellite.SnrDbHz is { } snr ? Invariant($" {snr, 2:0} dB-Hz") : " not tracked");
        if (satellite.Used)
        {
            line.Append(" used");
        }

        return line.ToString();
    }

    private static string Name(GnssSystem system) =>
        system switch
        {
            GnssSystem.Gps => "GPS",
            GnssSystem.Sbas => "SBAS",
            GnssSystem.Glonass => "GLONASS",
            GnssSystem.Qzss => "QZSS",
            GnssSystem.Unknown => "?",
            _ => system.ToString(), // Galileo, BeiDou, NavIC
        };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
