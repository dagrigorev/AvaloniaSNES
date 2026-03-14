using Microsoft.Extensions.Logging;

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

    /// <summary>Raised on the caller's thread when a new log entry is appended.</summary>
    public event EventHandler<LogEntry>? LogAdded;

    public DiagnosticLogSink(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
        _entries = new Queue<LogEntry>(maxEntries);
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
        }
        LogAdded?.Invoke(this, entry);
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName, this);

    public void Dispose() { }
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
