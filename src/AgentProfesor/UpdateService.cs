using Velopack;
using Velopack.Sources;

namespace AgentProfesor;

public sealed class UpdateService
{
    private readonly UpdateManager _manager;

    public UpdateService(UpdateConfig config)
    {
        _manager = new UpdateManager(new GithubSource(config.RepoUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "dev";

    public event Action<string>? StatusChanged;

    public async Task<bool> CheckAndApplyAsync(bool restartAfterApply)
    {
        if (!_manager.IsInstalled)
        {
            StatusChanged?.Invoke("Neběží jako nainstalovaná verze (dev režim) – aktualizace přeskočena.");
            return false;
        }

        StatusChanged?.Invoke("Hledám novou verzi na GitHubu…");
        var newVersion = await _manager.CheckForUpdatesAsync();
        if (newVersion == null)
        {
            StatusChanged?.Invoke("Běží nejnovější verze.");
            return false;
        }

        StatusChanged?.Invoke($"Stahuju verzi {newVersion.TargetFullRelease.Version}…");
        await _manager.DownloadUpdatesAsync(newVersion);

        StatusChanged?.Invoke("Aktualizace stažena, instaluju…");
        if (restartAfterApply)
            _manager.ApplyUpdatesAndRestart(newVersion);
        else
            _manager.ApplyUpdatesAndExit(newVersion);

        return true;
    }
}
