using System.Diagnostics;
using System.Windows.Automation;
using AgentProfesor.Core;

namespace AgentProfesor;

/// <summary>
/// Polls the currently focused control (via UI Automation) roughly every
/// PollIntervalMilliseconds and feeds what it reads into a <see cref="CaptureCoordinator"/>,
/// which decides whether/when that turns into a stored version. Deliberately never touches raw
/// keystrokes or the clipboard – it only ever reads the text already visible in whatever control
/// has focus, the same way a screen reader would.
///
/// Runs on a background timer thread on purpose: Microsoft recommends calling UI Automation off
/// the UI thread to avoid re-entrancy/deadlock, and it keeps a slow UIA call from freezing the
/// tray/search UI.
/// </summary>
public sealed class CaptureService : IDisposable
{
    private readonly AppConfig _config;
    private readonly CaptureCoordinator _coordinator;
    private readonly FileLog _log;
    private readonly System.Threading.Timer _timer;
    private string? _lastDocKey;
    private volatile bool _paused;
    private int _pollInFlight;
    private int _consecutiveErrors;

    public CaptureService(AppConfig config, VersionStore store, FileLog log)
    {
        _config = config;
        _log = log;
        _coordinator = new CaptureCoordinator(store, config.Capture);
        _timer = new System.Threading.Timer(_ => SafePoll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsPaused => _paused;

    public void Start()
    {
        var interval = Math.Max(250, _config.Capture.PollIntervalMilliseconds);
        _timer.Change(0, interval);
        _log.Info($"Capture spuštěn (poll {interval} ms, pauza {_config.Capture.PauseAfterSeconds} s, periodicky {_config.Capture.PeriodicSnapshotSeconds} s)");
    }

    public void Pause()
    {
        _paused = true;
        _log.Info("Capture pozastaven uživatelem");
    }

    public void Resume()
    {
        _paused = false;
        _log.Info("Capture obnoven uživatelem");
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Flush();
        _log.Info("Capture zastaven");
    }

    public void Flush()
    {
        try
        {
            foreach (var result in _coordinator.FlushAll(DateTimeOffset.Now))
                LogStored(result, "flush");
        }
        catch (Exception ex)
        {
            _log.Warn("Flush při ukončování selhal", ex);
        }
    }

    private void SafePoll()
    {
        if (_paused)
            return;

        // System.Threading.Timer může spustit callback znovu, i když ten předchozí ještě běží
        // (UIA umí být pomalá). Bez tohohle by se pomalé polly hromadily na sebe.
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            return;

        try
        {
            Poll();
            _consecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            // UI Automation umí spadnout na leccos (zaseklé okno, COM chyba u cizí appky) –
            // jeden neúspěšný poll nesmí shodit agenta ani zastavit další zachytávání. Logujeme
            // ale jen prvních pár za sebou, ať se log nezaplaví, když je něco trvale rozbité.
            _consecutiveErrors++;
            if (_consecutiveErrors <= 3)
                _log.Warn($"Poll selhal (za sebou {_consecutiveErrors}×)", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _pollInFlight, 0);
        }
    }

    private void Poll()
    {
        var element = AutomationElement.FocusedElement;
        if (element == null)
        {
            HandleFocusChange(null);
            return;
        }

        string processName;
        try
        {
            processName = Process.GetProcessById(element.Current.ProcessId).ProcessName;
        }
        catch
        {
            HandleFocusChange(null);
            return;
        }

        var text = TryReadText(element);
        if (string.IsNullOrEmpty(text))
        {
            HandleFocusChange(null);
            return;
        }

        var windowTitle = FindWindowTitle(element) ?? processName;
        var docKey = $"{processName}|{windowTitle}";

        HandleFocusChange(docKey);

        var result = _coordinator.Observe(docKey, processName, windowTitle, text, DateTimeOffset.Now);
        LogStored(result, "capture");
    }

    private void HandleFocusChange(string? currentDocKey)
    {
        if (_lastDocKey != null && _lastDocKey != currentDocKey)
            LogStored(_coordinator.NotifyFocusLost(_lastDocKey, DateTimeOffset.Now), "switch");

        _lastDocKey = currentDocKey;
    }

    private void LogStored(CaptureResult? result, string source)
    {
        if (result == null || result.Outcome == CaptureOutcome.Unchanged)
            return;
        _log.Info($"Uložena verze #{result.VersionId} ({result.Outcome}) dokumentu {result.DocumentId} [{source}]");
    }

    private static string? TryReadText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObj) && textPatternObj is TextPattern textPattern)
        {
            try
            {
                return textPattern.DocumentRange.GetText(-1);
            }
            catch
            {
                // spadá to např. u některých virtualizovaných dlouhých dokumentů – zkusíme ValuePattern.
            }
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj) && valuePatternObj is ValuePattern valuePattern)
        {
            try
            {
                return valuePattern.Current.Value;
            }
            catch
            {
                // element mezitím zmizel/se stal nedostupným – přeskočit tenhle poll.
            }
        }

        return null;
    }

    private static string? FindWindowTitle(AutomationElement element)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            for (var i = 0; i < 25 && current != null; i++)
            {
                if (current.Current.ControlType == ControlType.Window && !string.IsNullOrWhiteSpace(current.Current.Name))
                    return current.Current.Name;
                current = walker.GetParent(current);
            }
        }
        catch
        {
            // procházení stromu UI Automation může u některých appek selhat – necháme fallback na processName.
        }

        return null;
    }

    public void Dispose() => _timer.Dispose();
}
