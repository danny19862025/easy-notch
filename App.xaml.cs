using System.Windows;
using EasyNotch.Services;
using Forms = System.Windows.Forms;

namespace EasyNotch;

public partial class App : Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;
    IslandWindow? _island;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One island per user session.
        _single = new Mutex(true, $"EasyNotch.SingleInstance.{Environment.UserName}", out bool first);
        if (!first) { Shutdown(); return; }

        var settings = Settings.Load();
        _island = new IslandWindow(settings);
        _island.Show();
        CreateTray();

        // EasyNotch.exe --open <media|focus|clipboard|notes|clock|settings> opens straight to a widget.
        int oi = Array.IndexOf(e.Args, "--open");
        if (oi >= 0 && oi + 1 < e.Args.Length)
        {
            string tab = e.Args[oi + 1];
            _island.Dispatcher.InvokeAsync(() => _island.OpenFromTray(tab), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    void CreateTray()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/easynotch.ico"))?.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Easy Notch", null, (_, _) => _island?.OpenFromTray());
        menu.Items.Add("Settings", null, (_, _) => _island?.OpenFromTray("settings"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream, 16, 16),
            Text = "Easy Notch",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) _island?.OpenFromTray(); };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _island?.FlushNotes();
        Noise.Set("off");
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _single?.Dispose();
        base.OnExit(e);
    }
}
