namespace AgentProfesor.Core;

public enum CaptureTrigger
{
    Pause,
    Periodic,
    Switch,
    Paste,
    Shutdown,
}

public sealed record CaptureConfig
{
    public int PauseAfterSeconds { get; init; } = 60;
    public int PeriodicSnapshotSeconds { get; init; } = 150;
    public int PollIntervalMilliseconds { get; init; } = 2000;
    public int LargePasteChars { get; init; } = 400;
    public int MinTextLength { get; init; } = 3;

    /// <summary>
    /// Názvy procesů (bez .exe, nerozlišuje velikost písmen), ve kterých se SMÍ zachytávat.
    /// Cokoliv jiného agent ignoruje – ani nepřečte text. Bezpečné výchozí chování pro
    /// ne-technického uživatele: verzují se jen dokumentové aplikace, ne banky/chaty/prohlížeč.
    ///
    /// null (klíč chybí) → použije se <see cref="DefaultAppAllowlist"/>.
    /// prázdné pole → nezachytává se nikde (uživatel si to vypnul).
    /// </summary>
    public string[]? AppAllowlist { get; init; }

    /// <summary>Výchozí sada dokumentových aplikací, když v konfiguraci není vlastní seznam.</summary>
    public static readonly string[] DefaultAppAllowlist =
    {
        "WINWORD",    // Microsoft Word
        "OUTLOOK",    // Microsoft Outlook
        "notepad",    // Poznámkový blok
        "wordpad",    // WordPad
        "notepad++",  // Notepad++
    };

    /// <summary>Smí se v této aplikaci (název procesu) zachytávat?</summary>
    public bool IsCaptureAllowed(string processName)
    {
        var list = AppAllowlist ?? DefaultAppAllowlist;
        foreach (var allowed in list)
        {
            if (string.Equals(allowed, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Seznam povolených aplikací, který se reálně použije (pro log/přehled).</summary>
    public IReadOnlyList<string> EffectiveAllowlist => AppAllowlist ?? DefaultAppAllowlist;
}

public sealed record StorageConfig
{
    public int FullKeyframeEveryNDiffs { get; init; } = 10;
    public int DiffToFullThresholdPercent { get; init; } = 60;
    public int CompressionLevel { get; init; } = 10;
    public int LogRotationMinutes { get; init; } = 60;
    public int LogFlushSeconds { get; init; } = 15;
}

public sealed record RetentionConfig
{
    public bool Enabled { get; init; } = true;
    public int KeepAllDays { get; init; } = 90;
    public int ThinToHourlyDays { get; init; } = 365;
    public bool KeepDailyBeyond { get; init; } = true;
    public int DailyRunHour { get; init; } = 4;
    public bool PruneLogFiles { get; init; }
    public int PruneLogFilesOlderThanDays { get; init; } = 730;
}

public sealed record DocumentInfo(long Id, string AppName, string WindowTitle, string DocKey, DateTimeOffset CreatedAt, DateTimeOffset LastCapturedAt);

public sealed record VersionSummary(
    long Id,
    long DocumentId,
    DateTimeOffset CapturedAt,
    CaptureTrigger Trigger,
    bool IsKeyframe,
    long? BaseVersionId,
    int CharCount,
    double? DiffPercent);

public sealed record SearchHit(long DocumentId, string AppName, string WindowTitle, long VersionId, DateTimeOffset CapturedAt, string Snippet);

public enum CaptureOutcome
{
    Unchanged,
    StoredAsKeyframe,
    StoredAsDiff,
}

public sealed record CaptureResult(CaptureOutcome Outcome, long DocumentId, long? VersionId);

public sealed record StorageStats(
    int DocumentCount,
    int VersionCount,
    int KeyframeCount,
    int DiffCount,
    long StoredBytes,
    long RawChars,
    DateTimeOffset? FirstCapture,
    DateTimeOffset? LastCapture);
