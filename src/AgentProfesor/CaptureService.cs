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
/// </summary>
public sealed class CaptureService : IDisposable
{
    private readonly AppConfig _config;
    private readonly CaptureCoordinator _coordinator;
    private readonly System.Threading.Timer _timer;
    private string? _lastDocKey;
    private volatile bool _paused;

    public event Action<string>? Activity;

    public CaptureService(AppConfig config, VersionStore store)
    {
        _config = config;
        _coordinator = new CaptureCoordinator(store, config.Capture);
        _timer = new System.Threading.Timer(_ => SafePoll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsPaused => _paused;

    public void Start() => _timer.Change(0, Math.Max(250, _config.Capture.PollIntervalMilliseconds));

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Flush();
    }

    public void Flush()
    {
        try
        {
            foreach (var result in _coordinator.FlushAll(DateTimeOffset.Now))
                NotifyIfStored(result);
        }
        catch
        {
            // Nejlepší možná snaha při vypínání appky – nesmí to zablokovat ukončení.
        }
    }

    private void SafePoll()
    {
        if (_paused)
            return;

        try
        {
            Poll();
        }
        catch
        {
            // UI Automation umí spadnout na leccos (zaseklé okno, COM chyba u cizí appky) –
            // jeden neúspěšný poll nesmí shodit agenta ani zastavit další zachytávání.
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
        NotifyIfStored(result);
    }

    private void HandleFocusChange(string? currentDocKey)
    {
        if (_lastDocKey != null && _lastDocKey != currentDocKey)
            NotifyIfStored(_coordinator.NotifyFocusLost(_lastDocKey, DateTimeOffset.Now));

        _lastDocKey = currentDocKey;
    }

    private void NotifyIfStored(CaptureResult? result)
    {
        if (result == null || result.Outcome == CaptureOutcome.Unchanged)
            return;
        Activity?.Invoke($"Verze #{result.VersionId} ({result.Outcome}) – dokument {result.DocumentId}");
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
