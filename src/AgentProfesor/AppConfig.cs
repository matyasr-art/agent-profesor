using System.IO;
using System.Text.Json;
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

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonDocument.Parse(json).RootElement;
            if (root.TryGetProperty("AgentProfesor", out var section))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(section.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                    return config;
            }
        }
        catch
        {
            // Poškozený nebo chybějící appsettings.json → jedou se výchozí hodnoty.
        }

        return new AppConfig();
    }
}
