namespace AgentProfesor.Core;

/// <summary>
/// Realistic sample history for showing the engine off without a Windows agent feeding it:
/// one proposal document edited in six steps (small edits, a price change, a large paste that
/// forces a keyframe, a rewrite) plus one e-mail, so search has more than one source to hit.
/// Used by tools/VersionDemo and by the dashboard's --demo mode. Never runs on its own – it only
/// seeds a store that is explicitly asked to be seeded.
/// </summary>
public static class DemoData
{
    public const string ProposalTitle = "Nabídka Zikmundov – DRAFT.docx";

    public static long Seed(VersionStore store, DateTimeOffset? start = null)
    {
        var t = start ?? new DateTimeOffset(2026, 9, 5, 9, 12, 0, TimeSpan.FromHours(2));
        const string app = "WINWORD";
        var docKey = $"{app}|hwnd:demo-1";

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
            documentId = store.Capture(docKey, app, ProposalTitle, text, t, trigger).DocumentId;
        }

        store.Capture("OUTLOOK|hwnd:demo-2", "OUTLOOK", "Re: schůzka Zikmundov",
            "Dobrý den, potvrzuji schůzku ve čtvrtek v 10:00. Cena a harmonogram viz nabídka.",
            t.AddMinutes(40), CaptureTrigger.Pause);

        store.Capture("notepad|hwnd:demo-3", "notepad", "poznámky.txt",
            "Úkoly na pátek:\n- poslat nabídku Zikmundov\n- zavolat do sklárny kvůli termínu\n- připravit podklady pro fakturaci",
            t.AddMinutes(55), CaptureTrigger.Pause);
        store.Capture("notepad|hwnd:demo-3", "notepad", "poznámky.txt",
            "Úkoly na pátek:\n- poslat nabídku Zikmundov ✔\n- zavolat do sklárny kvůli termínu\n- připravit podklady pro fakturaci\n- objednat vzorky skla",
            t.AddMinutes(90), CaptureTrigger.Pause);

        return documentId;
    }
}
