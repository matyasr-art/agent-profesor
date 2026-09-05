using System.Diagnostics;
using System.Drawing.Drawing2D;
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
    private readonly System.Windows.Forms.Timer _retentionTimer;
    private readonly AppConfig _config;
    private readonly FileLog _log;
    private readonly VersionStore _store;
    private readonly CaptureService _captureService;
    private readonly GlobalHotkey? _hotkey;
    private Icon? _ownedIcon;
    private SearchForm? _searchForm;
    private DateOnly _lastRetentionRun = DateOnly.MinValue;

    public TrayContext(AppConfig config, FileLog log)
    {
        _config = config;
        _log = log;
        _updateService = new UpdateService(_config.Update);

        Directory.CreateDirectory(_config.ResolveDataDirectory());
        _store = new VersionStore(_config.DatabasePath(), _config.Storage);
        _log.Info($"Databáze: {_config.DatabasePath()}");
        _captureService = new CaptureService(_config, _store, _log);

        _statusItem = new ToolStripMenuItem($"AgentProfesor {_updateService.CurrentVersion}") { Enabled = false };
        var searchItem = new ToolStripMenuItem($"Hledat  ({_config.Ui.Hotkey})", null, (_, _) => ShowSearch());
        _pauseItem = new ToolStripMenuItem("Pozastavit záznam", null, (_, _) => TogglePause());
        var logsItem = new ToolStripMenuItem("Otevřít složku s logy", null, (_, _) => OpenLogsFolder());
        var checkNowItem = new ToolStripMenuItem("Zkontrolovat aktualizace", null, async (_, _) => await RunUpdateCheckAsync(manual: true));
        var exitItem = new ToolStripMenuItem("Konec", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(searchItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(logsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(checkNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "AgentProfesor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowSearch();

        try
        {
            var msg = StartupRegistration.Apply(_config.Ui.RunAtLogon);
            _log.Info(msg);
        }
        catch (Exception ex)
        {
            _log.Warn("Nastavení autostartu selhalo", ex);
        }

        try
        {
            _hotkey = new GlobalHotkey(_config.Ui.Hotkey);
            _hotkey.Pressed += ShowSearch;
            _log.Info($"Globální zkratka zaregistrována: {_config.Ui.Hotkey}");
        }
        catch (Exception ex)
        {
            _hotkey = null;
            _log.Warn("Globální zkratka se nezaregistrovala", ex);
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

        // Retenci kontrolujeme jednou za hodinu (běh je pak jen jednou denně kolem DailyRunHour;
        // navíc catch-up hned po startu, kdyby byl počítač ve 4 ráno vypnutý).
        _retentionTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds };
        _retentionTimer.Tick += (_, _) => MaybeRunRetention();
        _retentionTimer.Start();
        MaybeRunRetention();

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

    private void OpenLogsFolder()
    {
        try
        {
            var dir = _config.LogDirectory();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn("Otevření složky s logy selhalo", ex);
        }
    }

    private void MaybeRunRetention()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastRetentionRun == today)
            return;

        // První běh po startu proběhne hned (catch-up); další dny až po naplánované hodině.
        var firstRunThisSession = _lastRetentionRun == DateOnly.MinValue;
        if (!firstRunThisSession && DateTime.Now.Hour < _config.Retention.DailyRunHour)
            return;

        _lastRetentionRun = today;

        // Běží na pozadí, ať neblokuje tray/UI (DB přístup je uvnitř VersionStore zamčený).
        Task.Run(() =>
        {
            try
            {
                var result = RetentionService.Run(_store, _config.Retention, DateTimeOffset.Now);
                if (result.DidAnything)
                    _log.Info($"Retence: sloučeno {result.Rebased}, smazáno {result.Deleted} verzí");
                else
                    _log.Info("Retence: nic k proklestění");
            }
            catch (Exception ex)
            {
                _log.Warn("Retence selhala", ex);
            }
        });
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
            _log.Warn("Kontrola aktualizací selhala", ex);
            if (manual)
                _trayIcon.ShowBalloonTip(5000, "AgentProfesor", $"Kontrola aktualizací selhala: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnUpdateStatusChanged(string status)
    {
        _statusItem.Text = status;
        _log.Info($"Aktualizace: {status}");
    }

    private Icon LoadTrayIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF));
                g.FillEllipse(brush, 1, 1, 30, 30);
                using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(Color.White);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("P", font, textBrush, new RectangleF(0, 0, 32, 32), format);
            }

            var handle = bmp.GetHicon();
            _ownedIcon = (Icon)Icon.FromHandle(handle).Clone();
            return _ownedIcon;
        }
        catch
        {
            // Když vlastní ikonu z nějakého důvodu nejde vykreslit, radši generická než žádný tray.
            return SystemIcons.Application;
        }
    }

    private void ExitApp()
    {
        _log.Info("Ukončuji na žádost uživatele (Konec)");
        _hotkey?.Dispose();
        _captureService.Stop();
        _captureService.Dispose();
        _updateTimer.Stop();
        _retentionTimer.Stop();
        _store.Dispose();
        _trayIcon.Visible = false;
        _ownedIcon?.Dispose();
        Application.Exit();
    }
}
