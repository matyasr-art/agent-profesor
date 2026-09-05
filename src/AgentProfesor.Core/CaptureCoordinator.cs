namespace AgentProfesor.Core;

/// <summary>
/// Decides WHEN a version gets committed to storage, given a stream of "here's the text
/// currently in the focused control" samples fed in roughly every PollIntervalMilliseconds.
/// This is deliberately OS-agnostic (it never touches UI Automation or a clock of its own) so
/// the whole pause/periodic/switch policy can be exercised with fake timestamps in tests,
/// independent of anything Windows-specific.
/// </summary>
public sealed class CaptureCoordinator
{
    private readonly VersionStore _store;
    private readonly CaptureConfig _config;
    private readonly Dictionary<string, TrackedDocument> _tracked = new();

    public CaptureCoordinator(VersionStore store, CaptureConfig config)
    {
        _store = store;
        _config = config;
    }

    private sealed class TrackedDocument
    {
        public string AppName = "";
        public string WindowTitle = "";
        public string LastObservedText = "";
        public DateTimeOffset? DirtySince;
    }

    /// <summary>Feed the text currently visible in the focused control for this document.</summary>
    public CaptureResult? Observe(string docKey, string appName, string windowTitle, string text, DateTimeOffset now)
    {
        if (text.Length < _config.MinTextLength)
            return null;

        if (!_tracked.TryGetValue(docKey, out var doc))
        {
            doc = new TrackedDocument();
            _tracked[docKey] = doc;
        }

        doc.AppName = appName;
        doc.WindowTitle = windowTitle;

        if (text == doc.LastObservedText)
        {
            if (doc.DirtySince != null && (now - doc.DirtySince.Value).TotalSeconds >= _config.PauseAfterSeconds)
                return Commit(docKey, doc, CaptureTrigger.Pause, now);
            return null;
        }

        var addedChars = Math.Max(0, text.Length - doc.LastObservedText.Length);
        doc.LastObservedText = text;
        doc.DirtySince ??= now;

        if (addedChars >= _config.LargePasteChars)
            return Commit(docKey, doc, CaptureTrigger.Paste, now);

        if ((now - doc.DirtySince.Value).TotalSeconds >= _config.PeriodicSnapshotSeconds)
            return Commit(docKey, doc, CaptureTrigger.Periodic, now);

        return null;
    }

    /// <summary>Focus left this document (window/app switch) — commit whatever's pending now
    /// instead of waiting for a pause that will never come while it's not focused.</summary>
    public CaptureResult? NotifyFocusLost(string docKey, DateTimeOffset now)
    {
        if (!_tracked.TryGetValue(docKey, out var doc) || doc.DirtySince == null)
            return null;
        return Commit(docKey, doc, CaptureTrigger.Switch, now);
    }

    /// <summary>Commit every document with unsaved changes, e.g. on app shutdown.</summary>
    public IReadOnlyList<CaptureResult> FlushAll(DateTimeOffset now)
    {
        var results = new List<CaptureResult>();
        foreach (var (docKey, doc) in _tracked)
        {
            if (doc.DirtySince != null)
                results.Add(Commit(docKey, doc, CaptureTrigger.Shutdown, now));
        }
        return results;
    }

    private CaptureResult Commit(string docKey, TrackedDocument doc, CaptureTrigger trigger, DateTimeOffset now)
    {
        var result = _store.Capture(docKey, doc.AppName, doc.WindowTitle, doc.LastObservedText, now, trigger);
        doc.DirtySince = null;
        return result;
    }
}
