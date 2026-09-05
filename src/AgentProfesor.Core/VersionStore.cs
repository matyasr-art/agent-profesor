using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentProfesor.Core;

/// <summary>
/// SQLite-backed store for captured documents and their version history.
///
/// Each version is either a "keyframe" (full text, Brotli-compressed) or a "diff" against an
/// earlier version in the same document (a <see cref="LineDiff"/> edit script, also compressed).
/// Diff chains are kept short by <see cref="StorageConfig.FullKeyframeEveryNDiffs"/> and
/// re-based to a fresh keyframe whenever a change is too large to make a diff worthwhile
/// (<see cref="StorageConfig.DiffToFullThresholdPercent"/>), so reconstructing the latest text
/// for a document never has to replay more than N diffs.
///
/// A plaintext copy of every version's full text also goes into an FTS5 index for search –
/// versioning cares about storage efficiency, search does not, so keeping search independent
/// of the diff chain keeps both simpler.
/// </summary>
public sealed class VersionStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StorageConfig _storageConfig;

    public VersionStore(string dataSource, StorageConfig storageConfig)
    {
        _storageConfig = storageConfig;
        _connection = new SqliteConnection($"Data Source={dataSource}");
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                doc_key TEXT NOT NULL UNIQUE,
                app_name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_captured_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS versions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL REFERENCES documents(id),
                captured_at TEXT NOT NULL,
                trigger TEXT NOT NULL,
                is_keyframe INTEGER NOT NULL,
                base_version_id INTEGER NULL REFERENCES versions(id),
                diffs_since_keyframe INTEGER NOT NULL,
                content_compressed BLOB NOT NULL,
                char_count INTEGER NOT NULL,
                diff_percent REAL NULL
            );

            CREATE INDEX IF NOT EXISTS idx_versions_document ON versions(document_id, id);

            CREATE VIRTUAL TABLE IF NOT EXISTS versions_fts USING fts5(
                content, tokenize = 'unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public CaptureResult Capture(string docKey, string appName, string windowTitle, string fullText, DateTimeOffset capturedAt, CaptureTrigger trigger)
    {
        using var transaction = _connection.BeginTransaction();

        var doc = FindOrCreateDocument(docKey, appName, windowTitle, capturedAt, transaction);
        var latest = GetLatestVersionRow(doc.Id, transaction);

        if (latest != null)
        {
            var latestText = ReconstructText(doc.Id, latest.Value.Id, transaction);
            if (latestText == fullText)
            {
                TouchDocument(doc.Id, capturedAt, transaction);
                transaction.Commit();
                return new CaptureResult(CaptureOutcome.Unchanged, doc.Id, latest.Value.Id);
            }
        }

        long versionId;
        CaptureOutcome outcome;

        if (latest == null)
        {
            versionId = InsertKeyframe(doc.Id, fullText, capturedAt, trigger, diffsSinceKeyframe: 0, diffPercent: null, transaction);
            outcome = CaptureOutcome.StoredAsKeyframe;
        }
        else
        {
            var baseText = ReconstructText(doc.Id, latest.Value.Id, transaction);
            var baseLines = SplitLines(baseText);
            var newLines = SplitLines(fullText);
            var diffOps = LineDiff.Compute(baseLines, newLines);
            var changedRatio = LineDiff.ChangedRatio(diffOps, baseLines.Length);
            var diffsSinceKeyframe = latest.Value.DiffsSinceKeyframe + 1;

            var shouldKeyframe = changedRatio * 100 > _storageConfig.DiffToFullThresholdPercent
                                  || diffsSinceKeyframe >= _storageConfig.FullKeyframeEveryNDiffs;

            if (shouldKeyframe)
            {
                versionId = InsertKeyframe(doc.Id, fullText, capturedAt, trigger, diffsSinceKeyframe: 0, changedRatio, transaction);
                outcome = CaptureOutcome.StoredAsKeyframe;
            }
            else
            {
                var serializedDiff = JsonSerializer.Serialize(diffOps);
                var compressed = TextCompression.Compress(serializedDiff, _storageConfig.CompressionLevel);
                versionId = InsertVersionRow(doc.Id, capturedAt, trigger, isKeyframe: false, latest.Value.Id, diffsSinceKeyframe, compressed, fullText.Length, changedRatio, transaction);
                outcome = CaptureOutcome.StoredAsDiff;
            }
        }

        IndexForSearch(versionId, fullText, transaction);
        TouchDocument(doc.Id, capturedAt, transaction);
        transaction.Commit();

        return new CaptureResult(outcome, doc.Id, versionId);
    }

    public string GetLatestText(long documentId)
    {
        var latest = GetLatestVersionRow(documentId, null) ?? throw new InvalidOperationException($"Dokument {documentId} nemá žádnou verzi.");
        return ReconstructText(documentId, latest.Id, null);
    }

    public IReadOnlyList<DocumentInfo> ListDocuments()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, app_name, window_title, doc_key, created_at, last_captured_at FROM documents ORDER BY last_captured_at DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<DocumentInfo>();
        while (reader.Read())
        {
            result.Add(new DocumentInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5))));
        }
        return result;
    }

    public IReadOnlyList<VersionSummary> ListVersions(long documentId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, document_id, captured_at, trigger, is_keyframe, base_version_id, char_count, diff_percent
            FROM versions WHERE document_id = $docId ORDER BY id ASC
            """;
        cmd.Parameters.AddWithValue("$docId", documentId);
        using var reader = cmd.ExecuteReader();
        var result = new List<VersionSummary>();
        while (reader.Read())
        {
            result.Add(new VersionSummary(
                reader.GetInt64(0),
                reader.GetInt64(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                Enum.Parse<CaptureTrigger>(reader.GetString(3)),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                (int)reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        }
        return result;
    }

    public string GetVersionText(long versionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT document_id FROM versions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", versionId);
        var documentId = (long?)cmd.ExecuteScalar() ?? throw new InvalidOperationException($"Verze {versionId} neexistuje.");
        return ReconstructText(documentId, versionId, null);
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 30)
    {
        var matchExpression = BuildMatchExpression(query);
        if (matchExpression.Length == 0)
            return Array.Empty<SearchHit>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.id, d.app_name, d.window_title, v.id, v.captured_at,
                   snippet(versions_fts, 0, '[', ']', '…', 10) AS snip
            FROM versions_fts
            JOIN versions v ON v.id = versions_fts.rowid
            JOIN documents d ON d.id = v.document_id
            WHERE versions_fts MATCH $query
            ORDER BY rank
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", matchExpression);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        var result = new List<SearchHit>();
        while (reader.Read())
        {
            result.Add(new SearchHit(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetString(5)));
        }
        return result;
    }

    private static string BuildMatchExpression(string query)
    {
        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace("\"", ""))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*");

        return string.Join(' ', tokens);
    }

    private (long Id, string AppName, string WindowTitle) FindOrCreateDocument(string docKey, string appName, string windowTitle, DateTimeOffset now, SqliteTransaction tx)
    {
        using (var select = _connection.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = "SELECT id, app_name, window_title FROM documents WHERE doc_key = $key";
            select.Parameters.AddWithValue("$key", docKey);
            using var reader = select.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
        }

        using var insert = _connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO documents (doc_key, app_name, window_title, created_at, last_captured_at)
            VALUES ($key, $app, $title, $now, $now)
            RETURNING id
            """;
        insert.Parameters.AddWithValue("$key", docKey);
        insert.Parameters.AddWithValue("$app", appName);
        insert.Parameters.AddWithValue("$title", windowTitle);
        insert.Parameters.AddWithValue("$now", now.ToString("O"));
        var id = (long)insert.ExecuteScalar()!;
        return (id, appName, windowTitle);
    }

    private void TouchDocument(long documentId, DateTimeOffset now, SqliteTransaction tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE documents SET last_captured_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
    }

    private readonly record struct VersionRow(long Id, bool IsKeyframe, long? BaseVersionId, int DiffsSinceKeyframe);

    private VersionRow? GetLatestVersionRow(long documentId, SqliteTransaction? tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, is_keyframe, base_version_id, diffs_since_keyframe
            FROM versions WHERE document_id = $docId ORDER BY id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$docId", documentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new VersionRow(reader.GetInt64(0), reader.GetInt64(1) != 0, reader.IsDBNull(2) ? null : reader.GetInt64(2), (int)reader.GetInt64(3));
    }

    private string ReconstructText(long documentId, long versionId, SqliteTransaction? tx)
    {
        var chain = new List<(bool IsKeyframe, byte[] Content)>();
        long? cursor = versionId;

        while (cursor != null)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT is_keyframe, base_version_id, content_compressed FROM versions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", cursor.Value);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException($"Verze {cursor} nenalezena (dokument {documentId}).");

            var isKeyframe = reader.GetInt64(0) != 0;
            var baseVersionId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
            var content = (byte[])reader["content_compressed"];

            chain.Add((isKeyframe, content));
            cursor = isKeyframe ? null : baseVersionId;
        }

        chain.Reverse();

        var text = TextCompression.Decompress(chain[0].Content);
        for (var i = 1; i < chain.Count; i++)
        {
            var diffOps = JsonSerializer.Deserialize<List<DiffOp>>(TextCompression.Decompress(chain[i].Content))!;
            text = string.Join('\n', LineDiff.Apply(SplitLines(text), diffOps));
        }

        return text;
    }

    private long InsertKeyframe(long documentId, string fullText, DateTimeOffset capturedAt, CaptureTrigger trigger, int diffsSinceKeyframe, double? diffPercent, SqliteTransaction tx)
    {
        var compressed = TextCompression.Compress(fullText, _storageConfig.CompressionLevel);
        return InsertVersionRow(documentId, capturedAt, trigger, isKeyframe: true, baseVersionId: null, diffsSinceKeyframe, compressed, fullText.Length, diffPercent, tx);
    }

    private long InsertVersionRow(long documentId, DateTimeOffset capturedAt, CaptureTrigger trigger, bool isKeyframe, long? baseVersionId, int diffsSinceKeyframe, byte[] content, int charCount, double? diffPercent, SqliteTransaction tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO versions (document_id, captured_at, trigger, is_keyframe, base_version_id, diffs_since_keyframe, content_compressed, char_count, diff_percent)
            VALUES ($docId, $capturedAt, $trigger, $isKeyframe, $baseVersionId, $diffsSince, $content, $charCount, $diffPercent)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("$docId", documentId);
        cmd.Parameters.AddWithValue("$capturedAt", capturedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$trigger", trigger.ToString());
        cmd.Parameters.AddWithValue("$isKeyframe", isKeyframe ? 1 : 0);
        cmd.Parameters.AddWithValue("$baseVersionId", (object?)baseVersionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$diffsSince", diffsSinceKeyframe);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$charCount", charCount);
        cmd.Parameters.AddWithValue("$diffPercent", (object?)diffPercent ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    private void IndexForSearch(long versionId, string fullText, SqliteTransaction tx)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO versions_fts (rowid, content) VALUES ($id, $content)";
        cmd.Parameters.AddWithValue("$id", versionId);
        cmd.Parameters.AddWithValue("$content", fullText);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Rewrites a version in place as a self-contained keyframe. Used by retention thinning:
    /// the version kept as a bucket's representative must stop depending on anything the
    /// thinning is about to delete.
    /// </summary>
    public void RebaseToKeyframe(long versionId)
    {
        var text = GetVersionText(versionId);
        var compressed = TextCompression.Compress(text, _storageConfig.CompressionLevel);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE versions
            SET is_keyframe = 1, base_version_id = NULL, diffs_since_keyframe = 0, content_compressed = $content
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", versionId);
        cmd.Parameters.AddWithValue("$content", compressed);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes a version outright. Only safe to call on a version nothing else still depends on
    /// as a diff base (retention guarantees this by always rebasing the survivor first).
    /// </summary>
    public void DeleteVersion(long versionId)
    {
        using var tx = _connection.BeginTransaction();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM versions_fts WHERE rowid = $id";
            cmd.Parameters.AddWithValue("$id", versionId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM versions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", versionId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static string[] SplitLines(string text) => text.Split('\n');

    public void Dispose() => _connection.Dispose();
}
