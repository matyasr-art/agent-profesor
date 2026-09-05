using Microsoft.Win32;

namespace AgentProfesor;

/// <summary>
/// Autostart after logon via the per-user Run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>). Chosen over a Startup-folder
/// shortcut or a scheduled task because it needs no admin rights, is trivially correct, and is
/// easy for a tester to inspect/remove. Deliberately not using Velopack's own shortcut helper –
/// this stays in our control and works the same whether the app is Velopack-installed or run
/// straight from a folder.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AgentProfesor";

    /// <summary>Registers or unregisters autostart to match <paramref name="runAtLogon"/>.
    /// Returns a short description of what it did, for the log.</summary>
    public static string Apply(bool runAtLogon)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key == null)
            return "autostart: klíč v registru se nepodařilo otevřít";

        var existing = key.GetValue(ValueName) as string;

        if (!runAtLogon)
        {
            if (existing != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return "autostart: vypnuto (odebráno z registru)";
            }
            return "autostart: vypnuto (nebylo nastaveno)";
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return "autostart: nezjištěna cesta k .exe, přeskočeno";

        var desired = $"\"{exePath}\"";
        if (string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase))
            return "autostart: zapnuto (beze změny)";

        key.SetValue(ValueName, desired);
        return $"autostart: zapnuto → {desired}";
    }
}
