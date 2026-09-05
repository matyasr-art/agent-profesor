using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class RetentionServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly VersionStore _store;
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public RetentionServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agentprofesor-retention-test-{Guid.NewGuid():N}.db");
        _store = new VersionStore(_dbPath, new StorageConfig { FullKeyframeEveryNDiffs = 1000, DiffToFullThresholdPercent = 95 });
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private long SeedDocumentWithVersionEveryHourFor(string docKey, DateTimeOffset start, int hours)
    {
        long documentId = 0;
        for (var i = 0; i < hours; i++)
        {
            var at = start.AddHours(i);
            var result = _store.Capture(docKey, "notepad", "log.txt", $"řádek {i}", at, CaptureTrigger.Periodic);
            documentId = result.DocumentId;
        }
        return documentId;
    }

    [Fact]
    public void Recent_versions_within_KeepAllDays_are_left_untouched()
    {
        var docId = SeedDocumentWithVersionEveryHourFor("doc1", Now.AddDays(-2), hours: 10);
        var before = _store.ListVersions(docId).Count;

        RetentionService.Run(_store, new RetentionConfig { KeepAllDays = 90, ThinToHourlyDays = 365 }, Now);

        Assert.Equal(before, _store.ListVersions(docId).Count);
    }

    [Fact]
    public void Versions_older_than_KeepAllDays_are_thinned_to_one_per_hour()
    {
        // 5 versions inside the same hour, all older than KeepAllDays.
        var start = Now.AddDays(-100);
        var docId = 0L;
        for (var i = 0; i < 5; i++)
            docId = _store.Capture("doc1", "word", "dopis.docx", $"verze {i}", start.AddMinutes(i * 5), CaptureTrigger.Periodic).DocumentId;

        var result = RetentionService.Run(_store, new RetentionConfig { KeepAllDays = 90, ThinToHourlyDays = 365 }, Now);

        Assert.Equal(1, result.Rebased);
        Assert.Equal(4, result.Deleted);
        Assert.True(result.DidAnything);

        var survivors = _store.ListVersions(docId);
        Assert.Single(survivors);
        Assert.True(survivors[0].IsKeyframe, "přeživší verze v bucketu musí být samostatný keyframe.");
        Assert.Equal("verze 4", _store.GetVersionText(survivors[0].Id));
    }

    [Fact]
    public void Thinning_preserves_correct_reconstruction_for_surviving_versions_across_buckets()
    {
        var start = Now.AddDays(-100);
        var docId = 0L;
        // Two distinct hour-buckets, several versions each.
        for (var i = 0; i < 4; i++)
            docId = _store.Capture("doc1", "word", "dopis.docx", $"hodina0 verze{i}", start.AddMinutes(i * 5), CaptureTrigger.Periodic).DocumentId;
        for (var i = 0; i < 4; i++)
            docId = _store.Capture("doc1", "word", "dopis.docx", $"hodina1 verze{i}", start.AddHours(1).AddMinutes(i * 5), CaptureTrigger.Periodic).DocumentId;

        RetentionService.Run(_store, new RetentionConfig { KeepAllDays = 90, ThinToHourlyDays = 365 }, Now);

        var survivors = _store.ListVersions(docId);
        Assert.Equal(2, survivors.Count);
        Assert.Equal("hodina0 verze3", _store.GetVersionText(survivors[0].Id));
        Assert.Equal("hodina1 verze3", _store.GetVersionText(survivors[1].Id));
        Assert.Equal("hodina1 verze3", _store.GetLatestText(docId));
    }

    [Fact]
    public void Beyond_ThinToHourlyDays_thins_to_one_per_day_when_KeepDailyBeyond_is_true()
    {
        var start = Now.AddDays(-400);
        var docId = 0L;
        for (var i = 0; i < 6; i++)
            docId = _store.Capture("doc1", "notepad", "old.txt", $"stará verze {i}", start.AddHours(i * 3), CaptureTrigger.Periodic).DocumentId;

        RetentionService.Run(_store, new RetentionConfig { KeepAllDays = 90, ThinToHourlyDays = 365, KeepDailyBeyond = true }, Now);

        var survivors = _store.ListVersions(docId);
        Assert.Single(survivors);
        Assert.Equal("stará verze 5", _store.GetVersionText(survivors[0].Id));
    }

    [Fact]
    public void Beyond_ThinToHourlyDays_collapses_to_a_single_version_when_KeepDailyBeyond_is_false()
    {
        var start = Now.AddDays(-500);
        var docId = 0L;
        for (var i = 0; i < 5; i++)
            docId = _store.Capture("doc1", "notepad", "ancient.txt", $"pravěká verze {i}", start.AddDays(i * 10), CaptureTrigger.Periodic).DocumentId;

        RetentionService.Run(_store, new RetentionConfig { KeepAllDays = 90, ThinToHourlyDays = 365, KeepDailyBeyond = false }, Now);

        var survivors = _store.ListVersions(docId);
        Assert.Single(survivors);
        Assert.Equal("pravěká verze 4", _store.GetVersionText(survivors[0].Id));
    }

    [Fact]
    public void Disabled_retention_does_nothing()
    {
        var start = Now.AddDays(-500);
        var docId = 0L;
        for (var i = 0; i < 5; i++)
            docId = _store.Capture("doc1", "notepad", "ancient.txt", $"verze {i}", start.AddMinutes(i), CaptureTrigger.Periodic).DocumentId;

        var result = RetentionService.Run(_store, new RetentionConfig { Enabled = false }, Now);

        Assert.False(result.DidAnything);
        Assert.Equal(5, _store.ListVersions(docId).Count);
    }
}
