# AgentProfesor

Tray aplikace pro Windows (.NET 8, WinForms), postavená od nuly s jedním cílem
na prvním místě: **aktualizace přes GitHub, ne přes ruční přenášení
instalačních balíčků**.

## Co appka umí

- **Zachytávání textu** — přes UI Automation čte text z aktuálně
  fokusovaného ovládacího prvku (Word, Outlook, poznámkový blok, prohlížeč,
  chaty...), stejným způsobem jako to dělá čtečka obrazovky. Nesahá na
  klávesnici ani schránku — nejde o keylogger.
- **Verzování** — každá zachycená změna se uloží jako verze dokumentu: buď
  jako `keyframe` (celý text) nebo jako `diff` (jen rozdíl oproti
  předchozí verzi), podle nastavení v `Storage` (`FullKeyframeEveryNDiffs`,
  `DiffToFullThresholdPercent`). Verze vznikne při pauze v psaní
  (`PauseAfterSeconds`), po delší době nepřetržitého psaní
  (`PeriodicSnapshotSeconds`), při přepnutí okna, nebo při vložení velkého
  bloku textu (`LargePasteChars`).
- **Vyhledávání** — `Ctrl+Alt+Space` otevře okno, kde jde fulltextově hledat
  (i jen část slova) napříč historií všech zachycených dokumentů.
- **Rozhraní na historii verzí** — z výsledku hledání se dvojklikem/Enterem
  otevře okno se seznamem všech verzí daného dokumentu (kdy, proč, keyframe
  nebo diff), plným textem vybrané verze a barevně odlišeným rozdílem
  oproti předchozí verzi (`+`/`-`/beze změny).
- **Pauza/obnovení záznamu** — přes tray menu.
- **Retence** — `RetentionService` proklestí starou historii podle
  `Retention` (do `KeepAllDays` beze změny, pak zhuštění na 1 verzi/hodinu,
  po `ThinToHourlyDays` na 1 verzi/den nebo úplně). Spouští se sama: jednou
  po startu (catch-up) a pak denně kolem `DailyRunHour`, na pozadí.
- **Logování** — každé spuštění, uložená verze, chyba pollu, běh retence i
  aktualizace jdou do `%LocalAppData%\AgentProfesor\logs\agent-*.log`
  (rotace po `LogRotationMinutes`, každý řádek se hned flushne, ať přežije i
  pád). Neošetřené výjimky (i fatální) se zalogují, takže „agent někdy
  zmizel" už nebude bez stopy. Tray menu má „Otevřít složku s logy", ať
  tester log snadno pošle zpět.
- **Autostart po přihlášení** — `RunAtLogon` (výchozí zapnuto) zapisuje
  spouštění do `HKCU\...\Run` (bez admin práv, snadno zkontrolovatelné).
- **Aktualizace přes GitHub Releases** (Velopack) — viz níže.

Jádro (diffování, ukládání verzí, retence, vyhledávání, logování) je v
samostatném projektu `AgentProfesor.Core`, který neběží jen na Windows — má
46 jednotkových testů (`tests/AgentProfesor.Core.Tests`), které se pouštějí
i v CI na `ubuntu-latest` při každém pushi. `VersionStore` je thread-safe:
capture (vlastní vlákno), hledání (UI vlákno) i retence sáhnou na databázi
přes jeden zámek, takže souběh nemůže data poškodit.

**Co ověřeno a co ne:** jádro (diff/verze/retence/hledání/log) je pokryté
testy a reálně proběhlo — spustit se dá i mimo Windows demo nástrojem
(viz níže). UI Automation capture, globální klávesová zkratka, autostart a
tray appka jako celek jsou zatím ověřené jen tím, že se to zkompiluje a
spustí `dotnet publish` pro win-x64 (vývoj probíhá na Macu, který Windows
nemá) — reálné otestování na živém Windows (podle `CTI-ME-PRVNI.md` z
prvního testovacího kola) ještě čeká.

Protože se appka nedala vyzkoušet za běhu, prošla místo toho **adversariální
revizí kódu** (6 recenzentů po dimenzích + ověření každého nálezu). Z ní
vzešlé potvrzené vady jsou opravené: mj. stabilní identita dokumentu přes
handle okna (ne měnící se titulek), vynechání vlastních oken agenta z
záznamu, korektní dorénování běžícího pollu a retence při ukončení,
thread-safe koordinátor zachytávání a self-defending mazání verzí odolné
proti zpětnému skoku hodin.

## Jak vidět verzování bez Windows (demo)

`tools/VersionDemo` pohání skutečné jádro: nasimuluje psaní jednoho
dokumentu v několika verzích a vypíše seznam verzí, rozdíl mezi nimi a
výsledky hledání — plus vyexportuje `demo-data.json`. Běží kdekoliv:

```bash
dotnet run --project tools/VersionDemo -- demo-data.json
```

Z toho exportu je postavený klikací **náhled rozhraní** (jak vypadá okno
historie verzí a hledání ve Windows appce), takže verzování je vidět i před
prvním během na Windows.

## Jak funguje aktualizace

- Appka je nainstalovaná přes Velopack instalátor (`AgentProfesorSetup.exe`
  z GitHub Releases) do `%LocalAppData%\AgentProfesor`, bez potřeby admin
  práv.
- Při startu a pak každých `CheckIntervalHours` hodin (výchozí 6, nastavení
  v `appsettings.json`) se zeptá GitHubu (repo v `Update.RepoUrl`), jestli
  existuje novější verze.
- Pokud ano, stáhne ji a nainstaluje na pozadí, appka se sama restartuje do
  nové verze. Jde to i ručně přes tray menu → „Zkontrolovat aktualizace".
- Žádné další kopírování zipů testerům — stačí jednou poslat odkaz na
  instalátor z první release, každá další verze doletí sama.

## Jak vydat novou verzi

1. Uprav kód, zvyš `<Version>` v `src/AgentProfesor/AgentProfesor.csproj`
   (jen pro pořádek — skutečnou verzi releasu určuje git tag).
2. Commitni a označ tag podle verze:
   ```bash
   git tag v0.1.1
   git push origin v0.1.1
   ```
3. GitHub Actions (`.github/workflows/release.yml`) na `windows-latest`
   automaticky: publishne self-contained win-x64 build, zabalí ho přes
   Velopack (`vpk pack`) a nahraje na GitHub Releases (`vpk upload github`).
4. Nainstalovaní testeři dostanou update do `CheckIntervalHours` hodin,
   nebo hned přes „Zkontrolovat aktualizace" v tray menu.

Dá se to spustit i ručně bez tagu — záložka Actions → Release →
„Run workflow" a zadat verzi.

## První instalace pro testera

První verzi musí tester nainstalovat ručně — stáhne `AgentProfesor-win-Setup.exe`
z GitHub Releases stránky repa a spustí ho. Repo je **veřejné**, takže odkaz na
release stačí poslat a stažení i následné auto-aktualizace fungují bez tokenu
(privátní repo by bez autentizace vracelo 404 — proto je veřejné).

## Vlastní nastavení, které přežije aktualizaci

Zabalený `appsettings.json` leží v instalačním adresáři a Velopack ho při každé
aktualizaci přepíše, takže úpravy v něm by po updatu zmizely. Kdo si chce něco
změnit natrvalo (zkratka, retence, interval kontroly), založí
`%LocalAppData%\AgentProfesor\appsettings.json` a napíše do něj **jen ty klíče,
které mění** — appka je při startu přimerguje na výchozí hodnoty. Nové výchozí
hodnoty z pozdějších verzí tím pádem dál platí u všeho, čeho se tester nedotkl.

## Lokální build

```bash
dotnet publish src/AgentProfesor/AgentProfesor.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Balení přes `vpk pack` funguje jen na hostitelském OS, pro který se balí —
Windows balíček (`.exe` instalátor) tedy jde reálně zabalit jen na Windows
runneru (proto to dělá GitHub Actions, ne lokální Mac).
