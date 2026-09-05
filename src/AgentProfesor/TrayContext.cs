namespace AgentProfesor;

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly UpdateService _updateService;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly AppConfig _config;

    public TrayContext()
    {
        _config = AppConfig.Load();
        _updateService = new UpdateService(_config.Update);

        _statusItem = new ToolStripMenuItem($"AgentProfesor {_updateService.CurrentVersion}") { Enabled = false };
        var checkNowItem = new ToolStripMenuItem("Zkontrolovat aktualizace", null, async (_, _) => await RunUpdateCheckAsync(manual: true));
        var exitItem = new ToolStripMenuItem("Konec", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
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
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
