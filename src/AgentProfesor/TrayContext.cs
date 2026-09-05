using System.IO;
using AgentProfesor.Core;

namespace AgentProfesor;

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly UpdateService _updateService;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly AppConfig _config;
    private readonly VersionStore _store;
    private readonly CaptureService _captureService;
    private readonly GlobalHotkey? _hotkey;
    private SearchForm? _searchForm;

    public TrayContext()
    {
        _config = AppConfig.Load();
        _updateService = new UpdateService(_config.Update);

        Directory.CreateDirectory(_config.ResolveDataDirectory());
        _store = new VersionStore(_config.DatabasePath(), _config.Storage);
        _captureService = new CaptureService(_config, _store);

        _statusItem = new ToolStripMenuItem($"AgentProfesor {_updateService.CurrentVersion}") { Enabled = false };
        var searchItem = new ToolStripMenuItem($"Hledat  ({_config.Ui.Hotkey})", null, (_, _) => ShowSearch());
        _pauseItem = new ToolStripMenuItem("Pozastavit záznam", null, (_, _) => TogglePause());
        var checkNowItem = new ToolStripMenuItem("Zkontrolovat aktualizace", null, async (_, _) => await RunUpdateCheckAsync(manual: true));
        var exitItem = new ToolStripMenuItem("Konec", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(searchItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(checkNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AgentProfesor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowSearch();

        try
        {
            _hotkey = new GlobalHotkey(_config.Ui.Hotkey);
            _hotkey.Pressed += ShowSearch;
        }
        catch (Exception ex)
        {
            _hotkey = null;
            _trayIcon.ShowBalloonTip(5000, "AgentProfesor", $"Globální zkratka se nezaregistrovala: {ex.Message}", ToolTipIcon.Warning);
        }

        _captureService.Start();

        _updateService.StatusChanged += OnUpdateStatusChanged;

        _updateTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromHours(Math.Max(1, _config.Update.CheckIntervalHours)).TotalMilliseconds,
        };
        _updateTimer.Tick += async (_, _) => await RunUpdateCheckAsync(manual: false);
        _updateTimer.Start();

        if (_config.Update.CheckOnStartup)
            _ = RunUpdateCheckAsync(manual: false);
    }

    private void ShowSearch()
    {
        if (_searchForm is { IsDisposed: false })
        {
            _searchForm.Activate();
            return;
        }

        _searchForm = new SearchForm(_store);
        _searchForm.Show();
        _searchForm.Activate();
    }

    private void TogglePause()
    {
        if (_captureService.IsPaused)
        {
            _captureService.Resume();
            _pauseItem.Text = "Pozastavit záznam";
        }
        else
        {
            _captureService.Pause();
            _pauseItem.Text = "Obnovit záznam";
        }
    }

    private async Task RunUpdateCheckAsync(bool manual)
    {
        try
        {
            var updated = await _updateService.CheckAndApplyAsync(restartAfterApply: true);
            if (!updated && manual)
                _trayIcon.ShowBalloonTip(3000, "AgentProfesor", "Běží nejnovější verze.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (manual)
                _trayIcon.ShowBalloonTip(5000, "AgentProfesor", $"Kontrola aktualizací selhala: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnUpdateStatusChanged(string status)
    {
        _statusItem.Text = status;
    }

    private void ExitApp()
    {
        _hotkey?.Dispose();
        _captureService.Stop();
        _captureService.Dispose();
        _store.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
