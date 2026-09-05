using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class VersionStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly VersionStore _store;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public VersionStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agentprofesor-test-{Guid.NewGuid():N}.db");
        _store = new VersionStore(_dbPath, new StorageConfig
        {
            FullKeyframeEveryNDiffs = 5,
            DiffToFullThresholdPercent = 60,
            CompressionLevel = 5,
        });
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void First_capture_creates_a_keyframe()
    {
        var result = _store.Capture("doc1", "notepad", "Untitled", "první text", T0, CaptureTrigger.Pause);
        Assert.Equal(CaptureOutcome.StoredAsKeyframe, result.Outcome);
        Assert.Equal("první text", _store.GetLatestText(result.DocumentId));
    }

    [Fact]
    public void Identical_resubmission_is_a_no_op()
    {
        var first = _store.Capture("doc1", "notepad", "Untitled", "stejný text", T0, CaptureTrigger.Pause);
        var second = _store.Capture("doc1", "notepad", "Untitled", "stejný text", T0.AddMinutes(5), CaptureTrigger.Pause);

        Assert.Equal(CaptureOutcome.Unchanged, second.Outcome);
        Assert.Equal(first.VersionId, second.VersionId);
        Assert.Single(_store.ListVersions(first.DocumentId));
    }

    [Fact]
    public void Small_edit_is_stored_as_a_diff()
    {
        var lines = Enumerable.Range(1, 10).Select(i => $"Řádek {i}").ToArray();
        var first = _store.Capture("doc1", "word", "smlouva.docx", string.Join('\n', lines), T0, CaptureTrigger.Pause);

        var edited = (string[])lines.Clone();
        edited[4] = "Řádek 5 upraven";
        var second = _store.Capture("doc1", "word", "smlouva.docx", string.Join('\n', edited), T0.AddMinutes(1), CaptureTrigger.Pause);

        Assert.Equal(CaptureOutcome.StoredAsDiff, second.Outcome);

        var versions = _store.ListVersions(first.DocumentId);
        Assert.Equal(2, versions.Count);
        Assert.True(versions[0].IsKeyframe);
        Assert.False(versions[1].IsKeyframe);
        Assert.Equal(versions[0].Id, versions[1].BaseVersionId);
    }

    [Fact]
    public void Large_rewrite_forces_a_fresh_keyframe_even_as_the_first_diff()
    {
        var first = _store.Capture("doc1", "word", "dopis.docx", "krátký text", T0, CaptureTrigger.Pause);
        var second = _store.Capture("doc1", "word", "dopis.docx",
            "Zcela přepsaný, mnohem delší dokument, který nemá s původním obsahem skoro nic společného, protože byl celý přepsán od začátku do konce.",
            T0.AddMinutes(1), CaptureTrigger.Pause);

        Assert.Equal(CaptureOutcome.StoredAsKeyframe, second.Outcome);
    }

    [Fact]
    public void Enough_diffs_force_a_new_keyframe_per_FullKeyframeEveryNDiffs()
    {
        var docId = _store.Capture("doc1", "word", "deník.docx", "verze 0", T0, CaptureTrigger.Pause).DocumentId;

        for (var i = 1; i <= 5; i++)
            _store.Capture("doc1", "word", "deník.docx", $"verze 0\nřádek {i}", T0.AddMinutes(i), CaptureTrigger.Pause);

        var versions = _store.ListVersions(docId);
        Assert.Equal(6, versions.Count);
        // FullKeyframeEveryNDiffs = 5: the 5th diff in a row must re-keyframe instead of chaining further.
        Assert.True(versions[^1].IsKeyframe, "5. diff v řadě měl vynutit nový keyframe.");
    }

    [Fact]
    public void GetLatestText_reconstructs_correctly_through_a_diff_chain()
    {
        var docId = _store.Capture("doc1", "notepad", "todo.txt", "úkol 1", T0, CaptureTrigger.Pause).DocumentId;
        _store.Capture("doc1", "notepad", "todo.txt", "úkol 1\núkol 2", T0.AddMinutes(1), CaptureTrigger.Pause);
        _store.Capture("doc1", "notepad", "todo.txt", "úkol 1\núkol 2\núkol 3", T0.AddMinutes(2), CaptureTrigger.Pause);

        Assert.Equal("úkol 1\núkol 2\núkol 3", _store.GetLatestText(docId));
    }

    [Fact]
    public void GetVersionText_returns_the_historical_text_of_an_older_version_not_just_latest()
    {
        var first = _store.Capture("doc1", "notepad", "todo.txt", "úkol 1", T0, CaptureTrigger.Pause);
        _store.Capture("doc1", "notepad", "todo.txt", "úkol 1\núkol 2", T0.AddMinutes(1), CaptureTrigger.Pause);

        Assert.Equal("úkol 1", _store.GetVersionText(first.VersionId!.Value));
    }

    [Fact]
    public void Deleting_a_version_that_others_depend_on_rebases_them_first()
    {
        // Řetězec: v1 keyframe → v2 diff(base v1) → v3 diff(base v2).
        var v1 = _store.Capture("doc1", "notepad", "todo.txt", "úkol 1", T0, CaptureTrigger.Pause);
        _store.Capture("doc1", "notepad", "todo.txt", "úkol 1\núkol 2", T0.AddMinutes(1), CaptureTrigger.Pause);
        var v3 = _store.Capture("doc1", "notepad", "todo.txt", "úkol 1\núkol 2\núkol 3", T0.AddMinutes(2), CaptureTrigger.Pause);

        var v2id = _store.ListVersions(v1.DocumentId)[1].Id;

        // Smazání báze (v2), na které visí v3, nesmí v3 poškodit – v3 se musí nejdřív
        // přerebasovat na keyframe. (Chrání proti osiření diffu při zpětném skoku hodin.)
        _store.DeleteVersion(v2id);

        Assert.Equal("úkol 1\núkol 2\núkol 3", _store.GetVersionText(v3.VersionId!.Value));
        var remaining = _store.ListVersions(v1.DocumentId);
        Assert.Equal(2, remaining.Count);
        Assert.True(remaining[^1].IsKeyframe, "v3 měla být po smazání své báze přerebasována na keyframe.");
    }

    [Fact]
    public void Search_finds_a_version_containing_the_word()
    {
        _store.Capture("doc1", "outlook", "Re: schůzka", "Ahoj, potvrzuji schůzku na čtvrtek v 10:00.", T0, CaptureTrigger.Pause);
        _store.Capture("doc2", "notepad", "poznámky.txt", "Nákupní seznam: mléko, chleba, vejce", T0.AddMinutes(1), CaptureTrigger.Pause);

        var hits = _store.Search("schůzku");
        Assert.Single(hits);
        Assert.Equal("outlook", hits[0].AppName);
    }

    [Fact]
    public void Search_matches_partial_word_prefix()
    {
        _store.Capture("doc1", "teams", "Chat s Petrem", "potvrzuji objednávku materiálu na sklad", T0, CaptureTrigger.Pause);

        var hits = _store.Search("objedn");
        Assert.Single(hits);
    }

    [Fact]
    public void Multiple_edits_with_pauses_produce_multiple_searchable_versions()
    {
        var docId = _store.Capture("doc1", "word", "nabídka.docx", "Verze pro klienta Alfa", T0, CaptureTrigger.Pause).DocumentId;
        _store.Capture("doc1", "word", "nabídka.docx", "Verze pro klienta Beta", T0.AddHours(1), CaptureTrigger.Pause);
        _store.Capture("doc1", "word", "nabídka.docx", "Verze pro klienta Gama", T0.AddHours(2), CaptureTrigger.Pause);

        Assert.Equal(3, _store.ListVersions(docId).Count);
        Assert.Single(_store.Search("Alfa"));
        Assert.Single(_store.Search("Beta"));
        Assert.Single(_store.Search("Gama"));
    }
}
