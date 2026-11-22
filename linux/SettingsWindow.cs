using EasyNotch.Pages;

namespace EasyNotch;

/// <summary>Settings in a normal, movable window (dark, matching the notch).</summary>
public sealed class SettingsWindow : Window
{
    public SettingsWindow(SettingsPage page)
    {
        Title = "Easy Notch Settings";
        Width = 660;
        Height = 460;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res.Brush("IslandBg");
        Icon = App.AppIcon;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeight.Bold });
        header.Children.Add(new TextBlock { Text = "Changes apply right away.", FontSize = 12, Foreground = Res.Brush("Sub"), Margin = new Thickness(0, 2, 0, 0) });
        DockPanel.SetDock(header, Dock.Top);

        var root = new DockPanel { Margin = new Thickness(28, 22, 22, 22) };
        root.Children.Add(header);
        root.Children.Add(page);
        Content = root;

        // Fill the page before the window shows: rows added during the first layout pass get lost.
        page.OnShown();
        // The page is reused next time; release it from this window when closing.
        Closed += (_, _) => root.Children.Remove(page);
    }
}
