using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class AboutPage : UserControl, IPage
{
    public static string RepoUrl => UpdateChecker.RepoUrl;

    /// <summary>Your Buy Me a Coffee page. The coffee button stays hidden while this is empty.</summary>
    public const string CoffeeUrl = "https://buymeacoffee.com/danny1986";

    readonly PageContext _ctx;

    public AboutPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        VersionText.Text = $"Version {UpdateChecker.CurrentText}";
        CoffeeBtn.Visibility = CoffeeUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ctx.Updates.Changed += () => { if (IsVisible) Refresh(); };
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
        GetUpdate.Visibility = u.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (u.UpdateAvailable) GetUpdate.Content = "Open the release page";
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    void GitHub_Click(object sender, RoutedEventArgs e) => Open(UpdateChecker.RepoUrl);
    void Coffee_Click(object sender, RoutedEventArgs e) { if (CoffeeUrl.Length > 0) Open(CoffeeUrl); }
    async void Check_Click(object sender, RoutedEventArgs e) => await _ctx.Updates.CheckAsync();
    void GetUpdate_Click(object sender, RoutedEventArgs e) => Open(_ctx.Updates.ReleaseUrl ?? $"{UpdateChecker.RepoUrl}/releases");
    void Settings_Click(object sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
