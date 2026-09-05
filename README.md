# AgentProfesor

Tray aplikace pro Windows (.NET 8, WinForms), postavená od nuly s jedním cílem
na prvním místě: **aktualizace přes GitHub, ne přes ruční přenášení
instalačních balíčků**.

## Co tohle zatím je a co ne

Tenhle scaffold obsahuje **jen distribuční/update infrastrukturu** — tray
ikonu s menu a napojení na Velopack, který kontroluje GitHub Releases,
stahuje novou verzi a potichu ji nainstaluje na pozadí.

Neobsahuje funkce z původního testovaného buildu (`Profesor.zip` — zachytávání
textu v appkách, `Ctrl+Alt+Space` vyhledávání, verzování dokumentů...). Ten
build byl jen zkompilované `.exe`, bez zdrojového kódu, takže se nedal
rozšířit — tenhle projekt na to navazuje jako čistý základ, do kterého se ty
funkce postupně doprogramují.

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

První verzi musí tester nainstalovat ručně — stáhne `AgentProfesorSetup.exe`
z GitHub Releases stránky repa a spustí ho. Repo je zatím privátní, takže
testeři potřebují buď být pozvaní jako collaboři, nebo se release stránka
zpřístupní jinak (např. zveřejnění repa, až na to bude čas).

## Lokální build

```bash
dotnet publish src/AgentProfesor/AgentProfesor.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Balení přes `vpk pack` funguje jen na hostitelském OS, pro který se balí —
Windows balíček (`.exe` instalátor) tedy jde reálně zabalit jen na Windows
runneru (proto to dělá GitHub Actions, ne lokální Mac).
