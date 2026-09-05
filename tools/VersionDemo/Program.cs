using System.Text;
using System.Text.Json;
using AgentProfesor.Core;

// Demo/smoke-tool: pohání SKUTEČNÉ jádro (AgentProfesor.Core) přesně tak, jak ho pohání živá
// appka – nasimuluje psaní a úpravy dokumentu v čase, nechá VersionStore rozhodnout o
// keyframe/diff a pak to celé vypíše + vyexportuje do JSON pro náhled rozhraní. Běží kdekoliv
// (net8.0), takže se verzování dá reálně vidět i mimo Windows.

var dbPath = Path.Combine(Path.GetTempPath(), $"agentprofesor-demo-{Guid.NewGuid():N}.db");
using var store = new VersionStore(dbPath, new StorageConfig
{
    FullKeyframeEveryNDiffs = 10,
    DiffToFullThresholdPercent = 60,
    CompressionLevel = 10,
});

var app = "WINWORD";
var title = "Nabídka Zikmundov – DRAFT.docx";
var docKey = $"{app}|{title}";
var t = new DateTimeOffset(2026, 9, 5, 9, 12, 0, TimeSpan.FromHours(2));

// Postupné úpravy jednoho dokumentu, jak by je psal člověk: první nástřel, dopsané odstavce,
// oprava čísla, vložení celé pasáže (velké „vložení textu"), přeformulování.
var edits = new (string Text, CaptureTrigger Trigger, int MinutesLater)[]
{
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o naše řešení.",
        CaptureTrigger.Periodic, 0),
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o naše řešení.\nRádi bychom navázali na naši schůzku.",
        CaptureTrigger.Pause, 3),
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o naše řešení.\nRádi bychom navázali na naši schůzku.\n\nCena za fázi 1 je 120 000 Kč.",
        CaptureTrigger.Pause, 7),
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o naše řešení.\nRádi bychom navázali na naši schůzku.\n\nCena za fázi 1 je 145 000 Kč.",
        CaptureTrigger.Pause, 12),
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o naše řešení.\nRádi bychom navázali na naši schůzku.\n\nCena za fázi 1 je 145 000 Kč.\n\nHarmonogram:\n- analýza a návrh (2 týdny)\n- vývoj a nasazení (4 týdny)\n- zkušební provoz a předání (2 týdny)",
        CaptureTrigger.Paste, 18),
    (
        "Nabídka pro Zikmundov\n\nDobrý den,\nděkujeme za zájem o spolupráci.\nNavazujeme na naši schůzku z minulého týdne.\n\nCena za fázi 1 je 145 000 Kč bez DPH.\n\nHarmonogram:\n- analýza a návrh (2 týdny)\n- vývoj a nasazení (4 týdny)\n- zkušební provoz a předání (2 týdny)",
        CaptureTrigger.Switch, 25),
};

long documentId = 0;
foreach (var (text, trigger, minutes) in edits)
{
    t = t.AddMinutes(minutes);
    var result = store.Capture(docKey, app, title, text, t, trigger);
    documentId = result.DocumentId;
}

// Druhý dokument, ať má hledání co najít napříč více zdroji.
store.Capture("OUTLOOK|Re: schůzka Zikmundov", "OUTLOOK", "Re: schůzka Zikmundov",
    "Dobrý den, potvrzuji schůzku ve čtvrtek v 10:00. Cena a harmonogram viz nabídka.",
    t.AddMinutes(40), CaptureTrigger.Pause);

var versions = store.ListVersions(documentId);

// ---- Textový výpis (co uvidíš i v terminálu) ----
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"Dokument: {title}  ({app})");
Console.WriteLine($"Zachyceno verzí: {versions.Count}");
Console.WriteLine();
Console.WriteLine("  #  Kdy         Typ        Důvod            Znaků");
Console.WriteLine("  -  ----------  ---------  ---------------  -----");
for (var i = 0; i < versions.Count; i++)
{
    var v = versions[i];
    Console.WriteLine($"  {i + 1,-2} {v.CapturedAt.LocalDateTime:HH:mm:ss}    {(v.IsKeyframe ? "Keyframe" : "Diff"),-9}  {TriggerLabel(v.Trigger),-15}  {v.CharCount,5}");
}

Console.WriteLine();
Console.WriteLine("Rozdíl verze 4 → verze 5 (tak, jak ho vykreslí okno historie):");
Console.WriteLine();
foreach (var (marker, line) in DiffLines(store.GetVersionText(versions[3].Id), store.GetVersionText(versions[4].Id)))
    Console.WriteLine($"  {marker} {line}");

Console.WriteLine();
Console.WriteLine("Hledání „harmonogram\":");
foreach (var hit in store.Search("harmonogram"))
    Console.WriteLine($"  • [{hit.AppName}] {hit.WindowTitle} ({hit.CapturedAt.LocalDateTime:HH:mm})  …{hit.Snippet}…");

Console.WriteLine();
Console.WriteLine("Hledání „145\" (i po částech):");
foreach (var hit in store.Search("145"))
    Console.WriteLine($"  • [{hit.AppName}] {hit.WindowTitle} ({hit.CapturedAt.LocalDateTime:HH:mm})  …{hit.Snippet}…");

// ---- JSON export pro náhled rozhraní ----
var export = new
{
    document = new { app, title, versionCount = versions.Count },
    versions = versions.Select((v, i) => new
    {
        number = i + 1,
        time = v.CapturedAt.LocalDateTime.ToString("dd.MM. HH:mm:ss"),
        type = v.IsKeyframe ? "Keyframe" : "Diff",
        trigger = TriggerLabel(v.Trigger),
        chars = v.CharCount,
        fullText = store.GetVersionText(v.Id),
        diff = i == 0
            ? null
            : DiffLines(store.GetVersionText(versions[i - 1].Id), store.GetVersionText(v.Id))
                .Select(d => new { marker = d.Marker, line = d.Line })
                .ToArray(),
    }).ToArray(),
    searches = new[]
    {
        SearchExport(store, "harmonogram"),
        SearchExport(store, "145"),
        SearchExport(store, "schůzk"),
    },
};

var outPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "demo-data.json");
File.WriteAllText(outPath, JsonSerializer.Serialize(export, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));
Console.WriteLine();
Console.WriteLine($"JSON pro náhled rozhraní zapsán do: {outPath}");

static object SearchExport(VersionStore store, string query) => new
{
    query,
    hits = store.Search(query).Select(h => new
    {
        app = h.AppName,
        title = h.WindowTitle,
        time = h.CapturedAt.LocalDateTime.ToString("dd.MM. HH:mm"),
        snippet = h.Snippet,
    }).ToArray(),
};

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
    var curr = current.Split('\n');
    var ops = LineDiff.Compute(prev, curr);
    var result = new List<(string, string)>();
    var cursor = 0;
    foreach (var op in ops)
    {
        switch (op.Type)
        {
            case DiffOpType.Equal:
                for (var i = 0; i < op.Count; i++)
                    result.Add((" ", prev[cursor + i]));
                cursor += op.Count;
                break;
            case DiffOpType.Delete:
                for (var i = 0; i < op.Count; i++)
                    result.Add(("-", prev[cursor + i]));
                cursor += op.Count;
                break;
            case DiffOpType.Insert:
                foreach (var line in op.Lines!)
                    result.Add(("+", line));
                break;
        }
    }
    return result;
}
