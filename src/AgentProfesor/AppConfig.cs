using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentProfesor.Core;

namespace AgentProfesor;

public sealed class UpdateConfig
{
    public string RepoUrl { get; set; } = "https://github.com/matyasr-art/agent-profesor";
    public int CheckIntervalHours { get; set; } = 6;
    public bool CheckOnStartup { get; set; } = true;
}

public sealed class UiConfig
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool RunAtLogon { get; set; } = true;
}

public sealed class AppConfig
{
    public string DataDirectory { get; set; } = @"%LOCALAPPDATA%\AgentProfesor";
    public string BackupExportDirectory { get; set; } = @"%LOCALAPPDATA%\AgentProfesor\export";
    public CaptureConfig Capture { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public RetentionConfig Retention { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
    public UpdateConfig Update { get; set; } = new();

    public string ResolveDataDirectory() => Environment.ExpandEnvironmentVariables(DataDirectory);

    public string DatabasePath() => Path.Combine(ResolveDataDirectory(), "agentprofesor.db");

    public string LogDirectory() => Path.Combine(ResolveDataDirectory(), "logs");

    /// <summary>
    /// Načte konfiguraci dvouvrstvě: zabalený appsettings.json (v instalačním adresáři) dává
    /// VÝCHOZÍ hodnoty, a pokud existuje uživatelský appsettings.json v DataDirectory, přepíše
    /// jím jen ty klíče, které tester skutečně změnil.
    ///
    /// Proč: zabalený soubor Velopack při každé aktualizaci přepíše novou verzí, takže úpravy
    /// v něm by testerovi po updatu zmizely. Uživatelský soubor v %LocalAppData% aktualizace
    /// přežije. A protože se jen mergují změněné klíče, dostane tester i nové výchozí hodnoty
    /// z novějších verzí u nastavení, kterých se nedotkl.
    /// </summary>
    public static AppConfig Load()
    {
        var bundled = ReadSection(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        if (bundled == null)
            return new AppConfig();

        try
        {
            // DataDirectory (kam se kouká na uživatelský override) se bere ze zabaleného configu,
            // aby nevznikla cyklická závislost „kde hledat konfiguraci, která říká kam kouknout".
            var dataDir = Environment.ExpandEnvironmentVariables(
                bundled["DataDirectory"]?.GetValue<string>() ?? @"%LOCALAPPDATA%\AgentProfesor");
            var userPath = Path.Combine(dataDir, "appsettings.json");

            var userSection = ReadSection(userPath);
            if (userSection != null)
                DeepMerge(bundled, userSection);

            var config = bundled.Deserialize<AppConfig>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (config != null)
                return config;
        }
        catch
        {
            // Poškozený uživatelský soubor apod. → jedou aspoň zabalené výchozí hodnoty.
        }

        return new AppConfig();
    }

    private static JsonObject? ReadSection(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return root?["AgentProfesor"] as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static void DeepMerge(JsonObject target, JsonObject overrides)
    {
        foreach (var (key, value) in overrides)
        {
            if (value is JsonObject overrideChild && target[key] is JsonObject targetChild)
                DeepMerge(targetChild, overrideChild);
            else
                target[key] = value?.DeepClone();
        }
    }
}
