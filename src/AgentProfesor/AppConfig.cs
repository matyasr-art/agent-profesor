using System.Text.Json;

namespace AgentProfesor;

public sealed class UpdateConfig
{
    public string RepoUrl { get; set; } = "https://github.com/matyasr-art/agent-profesor";
    public int CheckIntervalHours { get; set; } = 6;
    public bool CheckOnStartup { get; set; } = true;
}

public sealed class AppConfig
{
    public UpdateConfig Update { get; set; } = new();

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
