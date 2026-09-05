using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class CaptureCoordinatorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly VersionStore _store;
    private readonly CaptureCoordinator _coordinator;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly CaptureConfig Config = new()
    {
        PauseAfterSeconds = 60,
        PeriodicSnapshotSeconds = 150,
        MinTextLength = 3,
        LargePasteChars = 40,
    };

    public CaptureCoordinatorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agentprofesor-coord-test-{Guid.NewGuid():N}.db");
        _store = new VersionStore(_dbPath, new StorageConfig());
        _coordinator = new CaptureCoordinator(_store, Config);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Text_below_MinTextLength_is_ignored()
    {
        var result = _coordinator.Observe("doc1", "notepad", "Untitled", "ab", T0);
        Assert.Null(result);
    }

    [Fact]
    public void Continuous_typing_does_not_commit_before_the_periodic_threshold()
    {
        Assert.Null(_coordinator.Observe("doc1", "notepad", "Untitled", "ahoj", T0));
        Assert.Null(_coordinator.Observe("doc1", "notepad", "Untitled", "ahoj jak se", T0.AddSeconds(30)));
        Assert.Null(_coordinator.Observe("doc1", "notepad", "Untitled", "ahoj jak se mas", T0.AddSeconds(60)));
    }

    [Fact]
    public void Continuous_typing_past_the_periodic_threshold_commits()
    {
        _coordinator.Observe("doc1", "notepad", "Untitled", "ahoj", T0);
        _coordinator.Observe("doc1", "notepad", "Untitled", "ahoj jak se mas dnes", T0.AddSeconds(100));
        var result = _coordinator.Observe("doc1", "notepad", "Untitled", "ahoj jak se mas dnes ty", T0.AddSeconds(151));

        Assert.NotNull(result);
        Assert.Equal(CaptureOutcome.StoredAsKeyframe, result!.Outcome);
    }

    [Fact]
    public void Pause_after_a_change_commits_with_pause_trigger()
    {
        _coordinator.Observe("doc1", "notepad", "Untitled", "napsaný text", T0);

        // Same text, no pause yet.
        var tooSoon = _coordinator.Observe("doc1", "notepad", "Untitled", "napsaný text", T0.AddSeconds(30));
        Assert.Null(tooSoon);

        var afterPause = _coordinator.Observe("doc1", "notepad", "Untitled", "napsaný text", T0.AddSeconds(61));
        Assert.NotNull(afterPause);

        var versions = _store.ListVersions(afterPause!.DocumentId);
        Assert.Equal(CaptureTrigger.Pause, versions[^1].Trigger);
    }

    [Fact]
    public void Large_paste_commits_immediately_without_waiting_for_pause_or_periodic()
    {
        _coordinator.Observe("doc1", "notepad", "Untitled", "krátký start", T0);
        var pasted = "krátký start" + new string('x', 100);

        var result = _coordinator.Observe("doc1", "notepad", "Untitled", pasted, T0.AddSeconds(2));

        Assert.NotNull(result);
        var versions = _store.ListVersions(result!.DocumentId);
        Assert.Equal(CaptureTrigger.Paste, versions[^1].Trigger);
    }

    [Fact]
    public void Switching_focus_away_commits_pending_change_with_switch_trigger()
    {
        _coordinator.Observe("doc1", "notepad", "Untitled", "rozepsaná věta", T0);
        var result = _coordinator.NotifyFocusLost("doc1", T0.AddSeconds(5));

        Assert.NotNull(result);
        var versions = _store.ListVersions(result!.DocumentId);
        Assert.Equal(CaptureTrigger.Switch, versions[^1].Trigger);
    }

    [Fact]
    public void NotifyFocusLost_on_a_clean_document_does_nothing()
    {
        _coordinator.Observe("doc1", "notepad", "Untitled", "beze změny", T0);
        _coordinator.NotifyFocusLost("doc1", T0.AddSeconds(1));

        var result = _coordinator.NotifyFocusLost("doc1", T0.AddSeconds(2));
        Assert.Null(result);
    }

    [Fact]
    public void FlushAll_commits_every_dirty_document_on_shutdown()
    {
        _coordinator.Observe("doc1", "notepad", "a.txt", "první dokument", T0);
        _coordinator.Observe("doc2", "word", "b.docx", "druhý dokument", T0);

        var results = _coordinator.FlushAll(T0.AddSeconds(1));

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.NotNull(r.VersionId));
    }

    [Fact]
    public void Editing_the_same_document_multiple_times_with_pauses_yields_multiple_versions()
    {
        _coordinator.Observe("doc1", "word", "nabidka.docx", "odstavec jedna", T0);
        _coordinator.NotifyFocusLost("doc1", T0.AddSeconds(70));

        _coordinator.Observe("doc1", "word", "nabidka.docx", "odstavec jedna, dopsáno", T0.AddMinutes(10));
        _coordinator.NotifyFocusLost("doc1", T0.AddMinutes(10).AddSeconds(70));

        var hits = _store.Search("dopsáno");
        Assert.Single(hits);

        var docId = hits[0].DocumentId;
        Assert.Equal(2, _store.ListVersions(docId).Count);
    }
}
