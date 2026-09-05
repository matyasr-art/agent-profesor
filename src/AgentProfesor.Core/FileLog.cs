using System.Text;

namespace AgentProfesor.Core;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// Small append-only file logger with time-based rotation, writing to
/// <c>agent-yyyyMMdd-HHmmss.log</c> in the given directory. Deliberately flushes every line
/// (AutoFlush): for a Phase-1 test build the whole point of the log is to survive a crash, so
/// losing the last few buffered lines – exactly the ones describing the crash – would defeat it.
///
/// Thread-safe: capture (background thread), the UI, and retention all log through one instance.
/// Lives in Core (uses only System.IO) so it's shared and unit-testable off Windows.
/// </summary>
public sealed class FileLog : IDisposable
{
    private readonly string _directory;
    private readonly TimeSpan _rotateAfter;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    private StreamWriter? _writer;
    private DateTimeOffset _openedAt;
    private bool _disposed;

    public FileLog(string directory, int rotationMinutes, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _rotateAfter = TimeSpan.FromMinutes(Math.Max(1, rotationMinutes));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string? CurrentPath { get; private set; }

    public void Info(string message) => Write(LogLevel.Info, message, null);

    public void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, message, ex);

    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private void Write(LogLevel level, string message, Exception? ex)
    {
        var tag = level switch
        {
            LogLevel.Info => "INF",
            LogLevel.Warn => "WRN",
            _ => "ERR",
        };

        lock (_gate)
        {
            // Po Dispose (typicky při ukončování appky) už nic nezapisovat – jinak by
            // EnsureWriter při _writer == null otevřel ZCELA NOVÝ agent-*.log, který už nikdo
            // nezavře. Zpožděný zápis (např. z doznívajícího background tasku) se tiše zahodí.
            if (_disposed)
                return;

            try
            {
                var now = _clock();
                EnsureWriter(now);

                _writer!.Write(now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                _writer.Write(" [");
                _writer.Write(tag);
                _writer.Write("] ");
                _writer.WriteLine(message);

                if (ex != null)
                    _writer.WriteLine(ex);
            }
            catch
            {
                // Logování nikdy nesmí shodit appku – když selže zápis (plný disk, zamčený
                // soubor), radši o řádek přijdeme, než abychom kvůli logu spadli.
            }
        }
    }

    private void EnsureWriter(DateTimeOffset now)
    {
        if (_writer != null && now - _openedAt < _rotateAfter)
            return;

        _writer?.Dispose();
        Directory.CreateDirectory(_directory);
        CurrentPath = Path.Combine(_directory, $"agent-{now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(CurrentPath, append: true, Encoding.UTF8) { AutoFlush = true };
        _openedAt = now;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
