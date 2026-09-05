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
    // How many consecutive "nothing capturable in focus" polls before we treat the active
    // document as switched-away-from. A single such poll is usually a transient glitch (a click
    // into a menu/toolbar, a momentary UIA/COM hiccup) and shouldn't tear down – and prematurely
    // commit – the document the user is still editing.
    private const int NullReadGrace = 3;

    private readonly AppConfig _config;
    private readonly CaptureCoordinator _coordinator;
    private readonly FileLog _log;
    private readonly System.Threading.Timer _timer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private string? _lastDocKey;
    private int _nullReads;
    private volatile bool _paused;
    private volatile bool _stopping;
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
        // Zabraň dalším spuštěním a POČKEJ na právě běžící poll, než uděláme Flush a než se nad
        // námi disposne VersionStore. _timer.Change/Dispose samy o sobě na běžící callback
        // nečekají – bez tohohle by Flush (UI vlákno) závodil s Observe (poll vlákno) nad
        // coordinatorem a store by se zavřel pod běžícím Capture.
        _stopping = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);

        var spin = new SpinWait();
        while (Interlocked.CompareExchange(ref _pollInFlight, 0, 0) != 0)
            spin.SpinOnce();

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
        if (_paused || _stopping)
            return;

        // System.Threading.Timer může spustit callback znovu, i když ten předchozí ještě běží
        // (UIA umí být pomalá). Bez tohohle by se pomalé polly hromadily na sebe.
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            return;

        try
        {
            if (_stopping)
                return;
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
            NoCapturableDocument();
            return;
        }

        int processId;
        try
        {
            processId = element.Current.ProcessId;
        }
        catch
        {
            NoCapturableDocument();
            return;
        }

        // Nikdy nezachytávej vlastní okna (vyhledávací okno, okno historie) – jinak by se do
        // úložiště indexovaly zpět už uložené (a citlivé) texty a hledané výrazy.
        if (processId == _ownProcessId)
        {
            NoCapturableDocument();
            return;
        }

        string processName;
        try
        {
            processName = Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            NoCapturableDocument();
            return;
        }

        var (windowTitle, windowHandle) = FindWindow(element);
        var text = TryReadText(element);
        if (string.IsNullOrEmpty(text))
        {
            NoCapturableDocument();
            return;
        }

        var displayTitle = windowTitle ?? processName;
        // Identita dokumentu stojí na STABILNÍM handlu okna, ne na titulku – ten se u řady appek
        // mění za běhu (nesetřená hvězdička, průběžně vkládaný obsah v prohlížeči, počet
        // nepřečtených v Outlooku), což by jinak roztříštilo historii jednoho dokumentu na mnoho.
        // Handle zároveň rozliší dvě okna se shodným titulkem (dva „Untitled – Notepad").
        var docKey = windowHandle != 0
            ? $"{processName}|hwnd:{windowHandle}"
            : $"{processName}|title:{displayTitle}";

        OnCapturableDocument(docKey);

        var result = _coordinator.Observe(docKey, processName, displayTitle, text, DateTimeOffset.Now);
        LogStored(result, "capture");
    }

    private void OnCapturableDocument(string docKey)
    {
        _nullReads = 0;
        if (_lastDocKey != null && _lastDocKey != docKey)
            LogStored(_coordinator.NotifyFocusLost(_lastDocKey, DateTimeOffset.Now), "switch");
        _lastDocKey = docKey;
    }

    private void NoCapturableDocument()
    {
        if (_lastDocKey == null)
            return;

        // Přechodné nečitelné čtení (kliknutí do menu, chvilková UIA chyba) nemá hned strhnout
        // rozepsaný dokument – teprve po několika po sobě jdoucích prázdných pollech ho uzavřeme.
        _nullReads++;
        if (_nullReads < NullReadGrace)
            return;

        LogStored(_coordinator.NotifyFocusLost(_lastDocKey, DateTimeOffset.Now), "switch");
        _lastDocKey = null;
        _nullReads = 0;
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

    private static (string? Title, int Handle) FindWindow(AutomationElement element)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            for (var i = 0; i < 25 && current != null; i++)
            {
                if (current.Current.ControlType == ControlType.Window)
                {
                    var title = current.Current.Name;
                    return (string.IsNullOrWhiteSpace(title) ? null : title, current.Current.NativeWindowHandle);
                }
                current = walker.GetParent(current);
            }
        }
        catch
        {
            // procházení stromu UI Automation může u některých appek selhat – fallback níže.
        }

        return (null, 0);
    }

    public void Dispose() => _timer.Dispose();
}
