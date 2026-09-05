using AgentProfesor.Core;

// Reálný běžící dashboard nad jádrem AgentProfesor.Core + SQLite. Ukazuje historii verzí,
// diffy a fulltextové hledání jako živá webová aplikace (na rozdíl od WinForms oken není
// vázaná na Windows a v budoucnu ji může hostovat přímo tray agent).
//
// Bez argumentu běží v DEMO režimu (dočasná DB naplněná ukázkovými daty). S přepínačem
// `--db=<cesta>` se připojí na skutečnou databázi agenta.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var app = builder.Build();

var dbArg = args.FirstOrDefault(a => a.StartsWith("--db="))?.Substring("--db=".Length);
var isDemo = string.IsNullOrEmpty(dbArg);
var dbPath = isDemo
    ? Path.Combine(Path.GetTempPath(), $"agentprofesor-dashboard-{Guid.NewGuid():N}.db")
    : dbArg!;

// Nad živou DB agenta čteme read-only (a přes WAL), ať dashboard neblokuje běžícího agenta a
// nemůže mu data omylem změnit. V demo režimu musíme zapisovat (seed), takže read-write.
var store = new VersionStore(dbPath, new StorageConfig(), readOnly: !isDemo);
if (isDemo)
{
    DemoData.Seed(store);
    app.Logger.LogInformation("Dashboard v DEMO režimu, DB: {Db}", dbPath);
}
else
{
    app.Logger.LogInformation("Dashboard nad reálnou DB (read-only): {Db}", dbPath);
}

app.Lifetime.ApplicationStopping.Register(() => store.Dispose());

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/meta", () => Results.Json(new { demo = isDemo }));

app.MapGet("/api/stats", () =>
{
    var s = store.GetStats();
    return Results.Json(new
    {
        documents = s.DocumentCount,
        versions = s.VersionCount,
        keyframes = s.KeyframeCount,
        diffs = s.DiffCount,
        storedBytes = s.StoredBytes,
        rawChars = s.RawChars,
        firstCapture = s.FirstCapture,
        lastCapture = s.LastCapture,
    });
});

app.MapGet("/api/documents", () =>
{
    var docs = store.ListDocuments().Select(d =>
    {
        var versions = store.ListVersions(d.Id);
        return new
        {
            id = d.Id,
            app = d.AppName,
            title = d.WindowTitle,
            created = d.CreatedAt,
            lastCaptured = d.LastCapturedAt,
            versionCount = versions.Count,
        };
    });
    return Results.Json(docs);
});

app.MapGet("/api/documents/{id:long}/versions", (long id) =>
{
    var versions = store.ListVersions(id).Select((v, i) => new
    {
        number = i + 1,
        id = v.Id,
        capturedAt = v.CapturedAt,
        trigger = v.Trigger.ToString(),
        triggerLabel = TriggerLabel(v.Trigger),
        isKeyframe = v.IsKeyframe,
        chars = v.CharCount,
    });
    return Results.Json(versions);
});

app.MapGet("/api/versions/{id:long}/text", (long id) =>
{
    try { return Results.Text(store.GetVersionText(id)); }
    catch { return Results.NotFound(); }
});

// Diff proti zadané bázi (base = id předchozí verze). Bez base = první verze dokumentu.
app.MapGet("/api/versions/{id:long}/diff", (long id, long? @base) =>
{
    try
    {
        var current = store.GetVersionText(id);
        if (@base is null)
            return Results.Json(new { first = true, lines = Array.Empty<object>() });

        var previous = store.GetVersionText(@base.Value);
        var lines = DiffLines(previous, current).Select(l => new { marker = l.Marker, line = l.Line });
        return Results.Json(new { first = false, lines });
    }
    catch { return Results.NotFound(); }
});

app.MapGet("/api/search", (string? q) =>
{
    var hits = store.Search(q ?? "").Select(h => new
    {
        documentId = h.DocumentId,
        versionId = h.VersionId,
        app = h.AppName,
        title = h.WindowTitle,
        capturedAt = h.CapturedAt,
        snippet = h.Snippet,
    });
    return Results.Json(hits);
});

app.Run();

static string TriggerLabel(CaptureTrigger trigger) => trigger switch
{
    CaptureTrigger.Pause => "pauza v psaní",
    CaptureTrigger.Periodic => "průběžně",
    CaptureTrigger.Switch => "přepnutí okna",
    CaptureTrigger.Paste => "vložení textu",
    CaptureTrigger.Shutdown => "ukončení appky",
    _ => trigger.ToString(),
};

static List<(string Marker, string Line)> DiffLines(string previous, string current)
{
    var prev = previous.Split('\n');
    var ops = LineDiff.Compute(prev, current.Split('\n'));
    var result = new List<(string, string)>();
    var cursor = 0;
    foreach (var op in ops)
    {
        switch (op.Type)
        {
            case DiffOpType.Equal:
                for (var i = 0; i < op.Count; i++) result.Add((" ", prev[cursor + i]));
                cursor += op.Count;
                break;
            case DiffOpType.Delete:
                for (var i = 0; i < op.Count; i++) result.Add(("-", prev[cursor + i]));
                cursor += op.Count;
                break;
            case DiffOpType.Insert:
                foreach (var line in op.Lines!) result.Add(("+", line));
                break;
        }
    }
    return result;
}

// Zpřístupněno pro integrační testy (WebApplicationFactory<Program>).
public partial class Program { }
