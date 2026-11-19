using System.Globalization;

namespace AprsNet.AprsIsCli;

/// <summary>
/// The liveness file of <c>listen --health-file</c> and <c>serve --health-file</c>: one line holding the UTC time the
/// command last knew it was receiving, rewritten atomically, for <c>aprscli health --file</c> to check. A container
/// <c>HEALTHCHECK</c> can then use one command for either mode, with no HTTP port.
/// </summary>
/// <remarks>
/// The file is only ever brought up to date, never marked down: when the command stops receiving it simply goes
/// stale, and the check's <c>--max-age</c> decides when that counts as unhealthy. A file left by an earlier run is
/// removed at start and the file is removed at exit, so it can't vouch for a run that has not connected or has ended.
/// </remarks>
internal sealed class HealthFile
{
    /// <summary>Names the file when <c>--health-file</c> isn't given; the image sets it. Empty turns it off.</summary>
    public const string Variable = "APRSCLI_HEALTH_FILE";

    /// <summary>
    /// Writes come at most this often, so a busy feed doesn't rewrite the file for every line. Far below
    /// <see cref="DefaultMaxAge"/>.
    /// </summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often <c>serve</c> looks at its health and refreshes the file while it is up.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The age at which <c>health --file</c> calls the file stale. APRS-IS servers send a keepalive about every 20 s,
    /// so 90 s is several missed ones, as the client's own idle timeout.
    /// </summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(90);

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly TextWriter _error;
    private long? _lastWrite;
    private bool _warned;

    public HealthFile(string path, TimeProvider time, TextWriter error)
    {
        Path = path;
        _time = time;
        _error = error;
    }

    public string Path { get; }

    /// <summary>The <c>--health-file</c> path, else <see cref="Variable"/>'s; null when neither names one.</summary>
    public static string? Resolve(string? option, Func<string, string?> getEnvironmentVariable) =>
        option is { Length: > 0 } ? option
        : getEnvironmentVariable(Variable) is { Length: > 0 } fromEnvironment ? fromEnvironment
        : null;

    /// <summary>
    /// Writes the time now, unless the last write was under <see cref="MinInterval"/> ago and
    /// <paramref name="force"/> is false. Never throws: a file that can't be written is reported once on standard
    /// error, and the command carries on (its health check will say unhealthy).
    /// </summary>
    public void Touch(bool force = false)
    {
        lock (_lock)
        {
            if (!force && _lastWrite is { } last && _time.GetElapsedTime(last) < MinInterval)
            {
                return;
            }

            var temporary = Path + ".tmp";
            try
            {
                if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                // Written beside the file and renamed over it, so a check never reads a half-written line.
                File.WriteAllText(temporary, LineFormatter.Timestamp(_time.GetUtcNow()) + "\n");
                // On Windows this fails while a reader holds the file without delete sharing (TryRead shares it). A
                // failed write leaves _lastWrite as it was, so the next line tries again.
                File.Move(temporary, Path, overwrite: true);
                _lastWrite = _time.GetTimestamp();
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (!_warned)
                {
                    _warned = true;
                    _error.WriteLine($"aprscli: cannot write the health file '{Path}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>Removes the file, so it no longer says the command is alive. Never throws.</summary>
    public void Remove()
    {
        lock (_lock)
        {
            _lastWrite = null;
            try
            {
                File.Delete(Path);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Nothing to remove, or not ours to remove: either way a stale file fails the check by its age.
            }
        }
    }

    /// <summary>
    /// When the file says the command was last alive: the time written in it, or the file's modification time if it
    /// holds no readable time (a file someone touched by hand). False when it doesn't exist or can't be read.
    /// </summary>
    public static bool TryRead(string path, out DateTimeOffset lastAlive, out string error)
    {
        lastAlive = default;
        error = "";
        try
        {
            var text = ReadText(path).Trim();
            lastAlive = DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var written
            )
                ? written
                : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            error = $"no health file at {path}: the command has not connected yet, or isn't writing one.";
            return false;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"cannot read the health file {path}: {ex.Message}";
            return false;
        }
    }

    /// <summary>Reads the file sharing it fully, so the writer can rename the next one over it meanwhile (Windows).</summary>
    internal static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
