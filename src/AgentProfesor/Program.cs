using System.IO;
using System.Reflection;
using AgentProfesor.Core;
using Velopack;

namespace AgentProfesor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Musí běžet jako úplně první věc v Main – zpracovává Velopack
        // instalační/aktualizační příkazy (--veloapp-install apod.), než
        // se appka rozjede jako tray ikona.
        VelopackApp.Build().Run();

        var config = AppConfig.Load();

        FileLog log;
        try
        {
            Directory.CreateDirectory(config.LogDirectory());
            log = new FileLog(config.LogDirectory(), config.Storage.LogRotationMinutes);
        }
        catch
        {
            // Když ani nejde založit log (např. nepřístupný LOCALAPPDATA), radši běž bez něj,
            // než abys kvůli logu vůbec nenaběhl.
            log = new FileLog(Path.GetTempPath(), config.Storage.LogRotationMinutes);
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        log.Info($"=== AgentProfesor {version} start (PID {Environment.ProcessId}) ===");

        // Bez tohohle by neošetřená výjimka v UI vlákně agenta potichu zabila – přesně to,
        // čeho se testeři bojí. Takhle se pád aspoň zaloguje (a UI-vláknové výjimky appku
        // nepoloží).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Error("Neošetřená výjimka v UI vlákně", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Error("Neošetřená výjimka (fatální)", e.ExceptionObject as Exception);
            log.Info($"=== AgentProfesor {version} končí kvůli fatální výjimce ===");
        };

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(config, log));
            log.Info($"=== AgentProfesor {version} normální ukončení ===");
        }
        catch (Exception ex)
        {
            log.Error("Pád při startu tray kontextu", ex);
            throw;
        }
        finally
        {
            log.Dispose();
        }
    }
}
