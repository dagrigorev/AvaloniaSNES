using Microsoft.Extensions.Logging;
using System.Text;

namespace SnesEmulator.Infrastructure.Logging;

/// <summary>
/// Thread-safe in-memory log sink for the emulator's diagnostic panel.
/// Log entries are kept in a bounded circular buffer to avoid unbounded memory growth.
/// The UI can subscribe to the <see cref="LogAdded"/> event to update in real time.
/// </summary>
public sealed class DiagnosticLogSink : ILoggerProvider
{
    private readonly int _maxEntries;
    private readonly Queue<LogEntry> _entries;
    private readonly object _lock = new();
    private StreamWriter? _sessionWriter;
    private string? _currentSessionLogPath;

    /// <summary>Raised on the caller's thread when a new log entry is appended.</summary>
    public event EventHandler<LogEntry>? LogAdded;

    public DiagnosticLogSink(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
        _entries = new Queue<LogEntry>(maxEntries);
    }


    /// <summary>Absolute path to the currently active session log file, if any.</summary>
    public string? CurrentSessionLogPath
    {
        get
        {
            lock (_lock) return _currentSessionLogPath;
        }
    }

    /// <summary>Starts a new on-disk diagnostic log session for the specified ROM path.</summary>
    public string StartRomSession(string romFilePath)
    {
        if (string.IsNullOrWhiteSpace(romFilePath))
            throw new ArgumentException("ROM file path must be provided.", nameof(romFilePath));

        lock (_lock)
        {
            CloseSessionWriter_NoLock();

            string safeRomName = SanitizeFileName(Path.GetFileNameWithoutExtension(romFilePath));
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SnesEmulator",
                "logs",
                safeRomName);

            Directory.CreateDirectory(logsDir);

            _currentSessionLogPath = Path.Combine(logsDir, $"{safeRomName}_{stamp}.log");
            _sessionWriter = new StreamWriter(_currentSessionLogPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };

            _sessionWriter.WriteLine($"# SNES Emulator diagnostic session");
            _sessionWriter.WriteLine($"# ROM: {romFilePath}");
            _sessionWriter.WriteLine($"# Started: {DateTimeOffset.Now:O}");
            _sessionWriter.WriteLine();

            return _currentSessionLogPath;
        }
    }

    /// <summary>Stops the current on-disk session, if one is active.</summary>
    public void StopRomSession()
    {
        lock (_lock)
        {
            CloseSessionWriter_NoLock();
            _currentSessionLogPath = null;
        }
    }

    private void CloseSessionWriter_NoLock()
    {
        if (_sessionWriter is null)
            return;

        try
        {
            _sessionWriter.WriteLine();
            _sessionWriter.WriteLine($"# Session closed: {DateTimeOffset.Now:O}");
            _sessionWriter.Flush();
            _sessionWriter.Dispose();
        }
        catch
        {
        }
        finally
        {
            _sessionWriter = null;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        string sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "rom" : sanitized;
    }

    /// <summary>Returns a snapshot of recent log entries.</summary>
    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
            return _entries.ToArray();
    }

    /// <summary>Clears all log entries.</summary>
    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    internal void Append(LogEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= _maxEntries)
                _entries.Dequeue();
            _entries.Enqueue(entry);
            _sessionWriter?.WriteLine($"{entry.Timestamp:HH:mm:ss.fff} {entry.LevelIndicator} {entry.ShortCategory,-16} {entry.Message}");
        }
        LogAdded?.Invoke(this, entry);
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName, this);

    public void Dispose()
    {
        lock (_lock)
        {
            CloseSessionWriter_NoLock();
            _currentSessionLogPath = null;
        }
    }
}

/// <summary>
/// A single log entry for the diagnostic panel.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message)
{
    /// <summary>Short category name (last segment after the last dot).</summary>
    public string ShortCategory => Category.Contains('.')
        ? Category[(Category.LastIndexOf('.') + 1)..]
        : Category;

    public string LevelIndicator => Level switch
    {
        LogLevel.Trace       => "TRC",
        LogLevel.Debug       => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning     => "WRN",
        LogLevel.Error       => "ERR",
        LogLevel.Critical    => "CRT",
        _ => "???"
    };
}

internal sealed class DiagnosticLogger : ILogger
{
    private readonly string _category;
    private readonly DiagnosticLogSink _sink;

    public DiagnosticLogger(string category, DiagnosticLogSink sink)
    {
        _category = category;
        _sink = sink;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        if (exception != null)
            message += $" [{exception.GetType().Name}: {exception.Message}]";

        _sink.Append(new LogEntry(DateTimeOffset.Now, logLevel, _category, message));
    }
}
