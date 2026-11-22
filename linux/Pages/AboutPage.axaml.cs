using Avalonia.Threading;
using Avalonia.Interactivity;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class AboutPage : UserControl, IPage
{
    /// <summary>Your Buy Me a Coffee page. The coffee button stays hidden while this is empty.</summary>
    public const string CoffeeUrl = "https://buymeacoffee.com/danny1986";

    readonly PageContext _ctx;

    public AboutPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        VersionText.Text = $"Version {UpdateChecker.CurrentText}";
        ToolTip.SetTip(GitHubBtn, UpdateChecker.RepoUrl);
        CoffeeBtn.IsVisible = CoffeeUrl.Length > 0;
        _ctx.Updates.Changed += () => Dispatcher.UIThread.Post(() => { if (IsVisible && Parent is not null) Refresh(); });
    }

    public void OnShown() => Refresh();
    public void OnTick() { }

    void Refresh()
    {
        var u = _ctx.Updates;
        CheckBtn.IsEnabled = !u.Checking;
        UpdateStatus.Text = u.Status.Length > 0 ? u.Status
            : _ctx.Settings.LastUpdateCheck == default ? "Not checked yet."
            : $"Last checked {TimeAgo.Of(_ctx.Settings.LastUpdateCheck)}.";
        GetUpdate.IsVisible = u.UpdateAvailable;
        if (u.UpdateAvailable) GetUpdate.Content = "Open the release page";
    }

    void GitHub_Click(object? sender, RoutedEventArgs e) => LinuxDesktop.Open(UpdateChecker.RepoUrl);
    void Coffee_Click(object? sender, RoutedEventArgs e) { if (CoffeeUrl.Length > 0) LinuxDesktop.Open(CoffeeUrl); }
    async void Check_Click(object? sender, RoutedEventArgs e) => await _ctx.Updates.CheckAsync();
    void GetUpdate_Click(object? sender, RoutedEventArgs e) => LinuxDesktop.Open(_ctx.Updates.ReleaseUrl ?? $"{UpdateChecker.RepoUrl}/releases");
    void Settings_Click(object? sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
