using Avalonia.Threading;
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using EasyNotch.Services;

namespace EasyNotch;

public partial class App : Application
{
    IslandWindow? _island;
    TrayIcon? _tray;
    IClassicDesktopStyleApplicationLifetime? _desktop;
    readonly List<PosixSignalRegistration> _signals = [];
    bool _exiting;

    internal static WindowIcon AppIcon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://easy-notch/Assets/easynotch.png")));
    static WindowIcon? _icon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        _desktop = desktop;
        Anim.Enabled = LinuxDesktop.AnimationsEnabled();

        var settings = Settings.Load();
        _island = new IslandWindow(settings);
        _island.Show();
        CreateTray();

        // easy-notch --open <page> starts with that page open.
        if (Program.OpenArg(desktop.Args ?? []) is { } page)
            Dispatcher.UIThread.Post(() => _island.OpenFromTray(page), DispatcherPriority.ApplicationIdle);

        // A second launch asks this copy to open (a page of) the notch.
        Program.Listen(message =>
        {
            if (!message.StartsWith("open", StringComparison.Ordinal)) return;
            string arg = message[4..].Trim();
            _island.OpenFromTray(arg.Length > 0 ? arg : null);
        });

        // Logout, `kill` and the installer send SIGTERM: save and close like Quit.
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP })
            _signals.Add(PosixSignalRegistration.Create(signal, c => { c.Cancel = true; Dispatcher.UIThread.Post(Quit); }));

        desktop.ShutdownRequested += (_, _) => Cleanup();
        desktop.Exit += (_, _) => Cleanup();
    }

    void CreateTray()
    {
        var menu = new NativeMenu();
        void Item(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Open Easy Notch", () => _island?.OpenFromTray());
        Item("Settings", () => _island?.OpenFromTray("settings"));
        Item("About", () => _island?.OpenFromTray("about"));
        menu.Items.Add(new NativeMenuItemSeparator());
        Item("Quit", Quit);

        _tray = new TrayIcon { Icon = AppIcon, ToolTipText = "Easy Notch", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => _island?.OpenFromTray();
        TrayIcon.SetIcons(this, [_tray]);
    }

    public static void Quit()
    {
        if (Current is App app && app._desktop is { } d)
        {
            app.Cleanup();
            d.Shutdown();
        }
    }

    void Cleanup()
    {
        if (_exiting) return;
        _exiting = true;
        _island?.FlushNotes();
        Noise.Set("off");
        _tray?.Dispose();
        foreach (var s in _signals) s.Dispose();
    }
}
