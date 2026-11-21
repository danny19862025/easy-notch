using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        Background = (Brush)Application.Current.FindResource("IslandBg");
        Foreground = (Brush)Application.Current.FindResource("Fg");
        FontFamily = (FontFamily)Application.Current.FindResource("Ui");
        UseLayoutRounding = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/easynotch.ico")); } catch { }

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "Changes apply right away.", FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("Sub"), Margin = new Thickness(0, 2, 0, 0),
        });
        DockPanel.SetDock(header, Dock.Top);

        var root = new DockPanel { Margin = new Thickness(28, 22, 22, 22) };
        root.Children.Add(header);
        root.Children.Add(page);
        Content = root;

        SourceInitialized += (_, _) => DarkTitleBar();
        Loaded += (_, _) => page.OnShown();
        // The page is reused next time; release it from this window when closing.
        Closed += (_, _) => root.Children.Remove(page);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void DarkTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        int black = 0x000B0A0A; // COLORREF 0x00BBGGRR: caption matches the notch
        DwmSetWindowAttribute(h, 35 /* DWMWA_CAPTION_COLOR */, ref black, sizeof(int));
    }
}
