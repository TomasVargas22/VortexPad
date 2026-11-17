using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VortexPad.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, string Source, string Message, string? Exception);

public interface ILogSink
{
    /// <summary>Must not block: called from any thread, including the 1000 Hz loop.</summary>
    void Enqueue(in LogEntry entry);

    /// <summary>Waits until pending entries are written (tests / shutdown).</summary>
    void Flush(TimeSpan timeout);
}

/// <summary>
/// Global application log. Writing never blocks the caller (the sink queues it and writes on its own thread).
/// For errors that can repeat thousands of times per second (1000 Hz loop, HID reads), use
/// <see cref="ErrorThrottled"/>: it writes at most one line per interval and counts the suppressed ones.
/// </summary>
public static class VortexLog
{
    private static volatile ILogSink? _sink;
    private static readonly ConcurrentDictionary<string, ThrottleState> Throttles = new();

    private sealed class ThrottleState
    {
        public long LastWrittenTimestamp;
        public int Suppressed;
    }

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Configures the destination (null = discard). Flushes the previous sink first.</summary>
    public static void Configure(ILogSink? sink)
    {
        _sink?.Flush(TimeSpan.FromSeconds(1));
        _sink = sink;
        Throttles.Clear();
    }

    public static void Debug(string source, string message) => Write(LogLevel.Debug, source, message, null);
    public static void Info(string source, string message) => Write(LogLevel.Info, source, message, null);
    public static void Warn(string source, string message, Exception? ex = null) => Write(LogLevel.Warning, source, message, ex);
    public static void Error(string source, string message, Exception? ex = null) => Write(LogLevel.Error, source, message, ex);

    /// <summary>
    /// Logs at most once per <paramref name="interval"/> for each <paramref name="key"/>. When it logs again,
    /// it adds how many repetitions were suppressed in between. Returns true if it wrote.
    /// </summary>
    public static bool ErrorThrottled(string key, TimeSpan interval, string source, string message, Exception? ex = null)
    {
        if (_sink == null) return false;

        var state = Throttles.GetOrAdd(key, _ => new ThrottleState());
        long now = Stopwatch.GetTimestamp();
        long intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);

        int suppressed;
        lock (state)
        {
            if (state.LastWrittenTimestamp != 0 && now - state.LastWrittenTimestamp < intervalTicks)
            {
                state.Suppressed++;
                return false;
            }
            suppressed = state.Suppressed;
            state.Suppressed = 0;
            state.LastWrittenTimestamp = now;
        }

        string text = suppressed > 0 ? $"{message} (+{suppressed} repeticiones omitidas)" : message;
        Write(LogLevel.Error, source, text, ex);
        return true;
    }

    public static void Flush(TimeSpan timeout) => _sink?.Flush(timeout);

    private static void Write(LogLevel level, string source, string message, Exception? ex)
    {
        var sink = _sink;
        if (sink == null || level < MinimumLevel) return;
        sink.Enqueue(new LogEntry(DateTime.Now, level, source, message, ex?.ToString()));
    }

    public static string Format(in LogEntry e)
    {
        var sb = new StringBuilder(128);
        sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(' ').Append(e.Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            _ => "ERR"
        });
        sb.Append(" [").Append(e.Source).Append("] ").Append(e.Message);
        if (!string.IsNullOrEmpty(e.Exception))
        {
            sb.AppendLine().Append("    ").Append(e.Exception.Replace(Environment.NewLine, Environment.NewLine + "    "));
        }
        return sb.ToString();
    }
}

/// <summary>
/// File log with rotation: <c>vortexpad.log</c> → <c>vortexpad.1.log</c> … up to <c>maxFiles</c>.
/// Writes on a background thread; if the queue fills up (10,000 entries) it drops lines instead of blocking.
/// </summary>
public sealed class RotatingFileLogSink : ILogSink, IDisposable
{
    private readonly BlockingCollection<LogEntry> _queue = new(boundedCapacity: 10_000);
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly Thread _writer;
    private int _pending;
    private int _dropped;

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexPad", "Logs");

    public RotatingFileLogSink(string? directory = null, string baseName = "vortexpad", long maxBytes = 1_000_000, int maxFiles = 5)
    {
        _directory = directory ?? DefaultDirectory;
        _baseName = baseName;
        _maxBytes = Math.Max(1024, maxBytes);
        _maxFiles = Math.Max(1, maxFiles);
        Directory.CreateDirectory(_directory);

        _writer = new Thread(WriteLoop) { Name = "VortexPad Log Writer", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public string CurrentFilePath => Path.Combine(_directory, _baseName + ".log");

    public void Enqueue(in LogEntry entry)
    {
        Interlocked.Increment(ref _pending);
        if (!_queue.TryAdd(entry))
        {
            Interlocked.Decrement(ref _pending);
            Interlocked.Increment(ref _dropped);
        }
    }

    public void Flush(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pending) > 0 && DateTime.UtcNow < until)
        {
            Thread.Sleep(5);
        }
    }

    private void WriteLoop()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            try
            {
                int dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0)
                {
                    AppendLine(VortexLog.Format(new LogEntry(DateTime.Now, LogLevel.Warning, "Log",
                        $"Se descartaron {dropped} líneas por cola llena.", null)));
                }
                AppendLine(VortexLog.Format(entry));
            }
            catch
            {
                // The log must never take the app down (disk full, permissions…): the line is lost
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private void AppendLine(string line)
    {
        string path = CurrentFilePath;
        var info = new FileInfo(path);
        if (info.Exists && info.Length >= _maxBytes)
        {
            Rotate();
        }
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private void Rotate()
    {
        string Numbered(int i) => Path.Combine(_directory, $"{_baseName}.{i}.log");

        string oldest = Numbered(_maxFiles - 1);
        if (_maxFiles == 1)
        {
            File.Delete(CurrentFilePath);
            return;
        }
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = _maxFiles - 2; i >= 1; i--)
        {
            if (File.Exists(Numbered(i))) File.Move(Numbered(i), Numbered(i + 1));
        }
        File.Move(CurrentFilePath, Numbered(1));
    }

    public void Dispose()
    {
        Flush(TimeSpan.FromSeconds(2));
        _queue.CompleteAdding();
        _writer.Join(1000);
        _queue.Dispose();
    }
}
