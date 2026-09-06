namespace AgentProfesor.Core;

/// <summary>
/// Ukázková historie pro předvedení enginu bez běžícího agenta – sada dokumentů, jaké by reálně
/// měl na disku cílový uživatel (prof. Pavel Kolář, fyzioterapie a rehabilitace, 2. LF UK a FN
/// Motol): kazuistika, přednáška, revize kapitoly knihy, posudek, e-mail a poznámky. Hlavní
/// dokument (kazuistika) má šest verzí, aby bylo vidět verzování: drobné úpravy, velké vložení
/// bloku (vynutí keyframe) i přeformulování. Používá ji dashboard i tools/VersionDemo.
/// Nikdy se nespouští sama – jen naseeduje store, který o to výslovně požádá.
/// </summary>
public static class DemoData
{
    public const string MainDocumentTitle = "Kazuistika – chronická bolest bederní páteře.docx";

    public static long Seed(VersionStore store, DateTimeOffset? start = null)
    {
        // Pozn.: veškerý obsah je smyšlený a anonymizovaný, slouží jen jako ukázka do dema.
        var baseT = start ?? new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.FromHours(2));

        SeedDoc(store, "notepad", "demo-note", "úkoly.txt", baseT.AddDays(-2), new[]
        {
            ("Úkoly:\n- dokončit posudek pro lázeňskou péči\n- příprava přednášky na 2. LF UK\n- Centrum pohybové medicíny: rozpis konzultací",
                CaptureTrigger.Pause, 0),
            ("Úkoly:\n- dokončit posudek pro lázeňskou péči ✔\n- příprava přednášky na 2. LF UK\n- Centrum pohybové medicíny: rozpis konzultací\n- objednat pomůcky do ambulance",
                CaptureTrigger.Pause, 35),
        });

        SeedDoc(store, "WINWORD", "demo-lecture", "Přednáška 2. LF UK – Vývojová kineziologie.docx", baseT.AddDays(-2).AddHours(3), new[]
        {
            ("Vývojová kineziologie a posturální vývoj\n\nOsnova přednášky:\n1. Posturální ontogeneze v prvním roce života\n2. Hluboký stabilizační systém páteře",
                CaptureTrigger.Periodic, 0),
            ("Vývojová kineziologie a posturální vývoj\n\nOsnova přednášky:\n1. Posturální ontogeneze v prvním roce života\n2. Hluboký stabilizační systém páteře\n3. Klinické využití vývojových pozic v rehabilitaci\n4. Ukázky vyšetření a nácviku",
                CaptureTrigger.Pause, 22),
        });

        SeedDoc(store, "WINWORD", "demo-book", "Rehabilitace v klinické praxi – revize kapitoly.docx", baseT.AddDays(-1), new[]
        {
            ("Rehabilitace v klinické praxi\n\nRevize kapitoly: Vyšetření hlubokého stabilizačního systému\n\nDoplnit popis bráničního testu a testu nitrobřišního tlaku, přidat obrazovou dokumentaci.",
                CaptureTrigger.Pause, 0),
        });

        SeedDoc(store, "WINWORD", "demo-posudek", "Posudek – žádost o lázeňskou péči.docx", baseT.AddDays(-1).AddHours(2), new[]
        {
            ("Posudek\n\nVěc: žádost o lázeňskou léčebně rehabilitační péči\n\nPacient je dlouhodobě sledován pro chronické bolesti bederní páteře. Doporučuji rehabilitační pobyt zaměřený na stabilizaci páteře a korekci pohybových stereotypů.",
                CaptureTrigger.Pause, 0),
        });

        SeedDoc(store, "OUTLOOK", "demo-mail", "Re: Konzultace – bolesti bederní páteře", baseT.AddHours(-2), new[]
        {
            ("Dobrý den,\nděkuji za zaslanou dokumentaci. Popisované bolesti bederní páteře odpovídají oslabení hlubokého stabilizačního systému. Navrhuji konzultaci v Centru pohybové medicíny.\nS pozdravem\nPavel Kolář",
                CaptureTrigger.Pause, 0),
        });

        // Hlavní dokument – nejnovější (zobrazí se v přehledu první), 6 verzí.
        return SeedDoc(store, "WINWORD", "demo-kazuistika", MainDocumentTitle, baseT, new[]
        {
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře",
                CaptureTrigger.Periodic, 0),
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře\n\nAnamnéza:\nBolesti beder přibližně 8 měsíců, horší při delším sedu, ranní ztuhlost.",
                CaptureTrigger.Pause, 4),
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře\n\nAnamnéza:\nBolesti beder přibližně 8 měsíců, horší při delším sedu, ranní ztuhlost.\n\nVstupní vyšetření:\nOslabený hluboký stabilizační systém páteře, insuficience bráničního dýchání.",
                CaptureTrigger.Pause, 9),
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře\n\nAnamnéza:\nBolesti beder přibližně 8 měsíců, horší při delším sedu, ranní ztuhlost.\n\nVstupní vyšetření:\nOslabený hluboký stabilizační systém páteře, insuficience bráničního dýchání.\nOmezená flexe trupu, palpačně bolestivé paravertebrální svaly v úrovni L4/L5.",
                CaptureTrigger.Pause, 14),
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře\n\nAnamnéza:\nBolesti beder přibližně 8 měsíců, horší při delším sedu, ranní ztuhlost.\n\nVstupní vyšetření:\nOslabený hluboký stabilizační systém páteře, insuficience bráničního dýchání.\nOmezená flexe trupu, palpačně bolestivé paravertebrální svaly v úrovni L4/L5.\n\nTerapeutický plán:\n- nácvik bráničního dýchání a nitrobřišního tlaku (DNS)\n- aktivace hlubokého stabilizačního systému ve vývojových pozicích\n- korekce sedu a stereotypu chůze\n- frekvence: 1x týdně, 6 týdnů",
                CaptureTrigger.Paste, 20),
            ("Kazuistika\n\nPacientka: N. N., 42 let\nDiagnóza: chronická bolest bederní páteře\n\nAnamnéza:\nBolesti beder přibližně 8 měsíců, horší při delším sedu, ranní ztuhlost.\n\nVstupní vyšetření:\nOslabený hluboký stabilizační systém páteře, insuficience bráničního dýchání.\nOmezená flexe trupu, palpačně bolestivé paravertebrální svaly v úrovni L4/L5.\n\nTerapeutický plán:\n- nácvik bráničního dýchání a nitrobřišního tlaku metodou DNS\n- aktivace hlubokého stabilizačního systému ve vývojových pozicích\n- korekce sedu a pohybových stereotypů\n- frekvence: 1× týdně po dobu 6 týdnů, poté kontrolní vyšetření",
                CaptureTrigger.Switch, 28),
        });
    }

    private static long SeedDoc(VersionStore store, string app, string handleId, string title, DateTimeOffset start, (string Text, CaptureTrigger Trigger, int MinutesLater)[] versions)
    {
        var docKey = $"{app}|hwnd:{handleId}";
        var t = start;
        long documentId = 0;
        foreach (var (text, trigger, minutes) in versions)
        {
            t = t.AddMinutes(minutes);
            documentId = store.Capture(docKey, app, title, text, t, trigger).DocumentId;
        }
        return documentId;
    }
}
