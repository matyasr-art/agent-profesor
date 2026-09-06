using System.Text;
using System.Text.Json;
using AgentProfesor.Core;

// Demo/smoke-tool: pohání SKUTEČNÉ jádro (AgentProfesor.Core) přesně tak, jak ho pohání živá
// appka – nasadí sdílený demo seed (DemoData), nechá VersionStore rozhodnout o keyframe/diff a
// pak to celé vypíše + vyexportuje do JSON. Běží kdekoliv (net8.0), takže se verzování dá
// reálně vidět i mimo Windows.

var dbPath = Path.Combine(Path.GetTempPath(), $"agentprofesor-demo-{Guid.NewGuid():N}.db");
using var store = new VersionStore(dbPath, new StorageConfig
{
    FullKeyframeEveryNDiffs = 10,
    DiffToFullThresholdPercent = 60,
    CompressionLevel = 10,
});

var documentId = DemoData.Seed(store);
var versions = store.ListVersions(documentId);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"Dokument: {DemoData.MainDocumentTitle}");
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
Console.WriteLine("Hledání „stabiliza\":");
foreach (var hit in store.Search("stabiliza"))
    Console.WriteLine($"  • [{hit.AppName}] {hit.WindowTitle} ({hit.CapturedAt.LocalDateTime:HH:mm})  …{hit.Snippet}…");

var export = new
{
    document = new { app = "WINWORD", title = DemoData.MainDocumentTitle, versionCount = versions.Count },
    versions = versions.Select((v, i) => new
    {
        number = i + 1,
        time = v.CapturedAt.LocalDateTime.ToString("dd.MM. HH:mm:ss"),
        type = v.IsKeyframe ? "Keyframe" : "Diff",
        trigger = TriggerLabel(v.Trigger),
        chars = v.CharCount,
        fullText = store.GetVersionText(v.Id),
    }).ToArray(),
};

var outPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "demo-data.json");
File.WriteAllText(outPath, JsonSerializer.Serialize(export, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));
Console.WriteLine();
Console.WriteLine($"JSON zapsán do: {outPath}");

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
