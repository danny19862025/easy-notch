using Avalonia.Threading;
using System.Net.Sockets;
using System.Text;

namespace EasyNotch;

public static class Program
{
    static readonly string[] PageIds = ["clock", "weather", "media", "focus", "notes", "clipboard", "mixer", "about", "settings"];

    static FileStream? _lock;
    static Socket? _listener;
    static string _socketPath = "";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Easy Notch: a notch at the top of your screen.\n\n" +
                              "  easy-notch                 start (or open the notch if it's already running)\n" +
                              "  easy-notch --open <page>   open a page: " + string.Join(", ", PageIds) + "\n" +
                              "  easy-notch --version       print the version");
            return 0;
        }
        if (args.Contains("--version")) { Console.WriteLine("Easy Notch " + Services.UpdateChecker.CurrentText); return 0; }

        string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir && Directory.Exists(dir) ? dir : Path.GetTempPath();
        _socketPath = Path.Combine(runtime, "easy-notch.sock");
        string message = "open " + (OpenArg(args) ?? "");
        try
        {
            // FileShare.None is an advisory lock on Linux; it's released automatically if the app dies.
            _lock = new FileStream(Path.Combine(runtime, "easy-notch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // One notch per session: hand the request to the running copy.
            if (Send(message)) return 0;
            Console.Error.WriteLine("Easy Notch is already running.");
            return 1;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            _listener?.Dispose();
            try { File.Delete(_socketPath); } catch (IOException) { }
            _lock.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .With(new X11PlatformOptions { WmClass = "easy-notch" })
        .LogToTrace();

    /// <summary>The page after --open, if it's one we know.</summary>
    internal static string? OpenArg(string[] args)
    {
        int i = Array.IndexOf(args, "--open");
        return i >= 0 && i + 1 < args.Length && PageIds.Contains(args[i + 1]) ? args[i + 1] : null;
    }

    static bool Send(string message)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            socket.Send(Encoding.UTF8.GetBytes(message + "\n"));
            return true;
        }
        catch (SocketException) { return false; }
    }

    /// <summary>Messages from later launches ("open clock", "open " …) arrive here, on the UI thread.</summary>
    internal static void Listen(Action<string> received)
    {
        try
        {
            try { File.Delete(_socketPath); } catch (IOException) { }
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _listener.Listen(4);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException) { _listener = null; return; }

        var listener = _listener;
        new Thread(() =>
        {
            var buffer = new byte[128];
            while (true)
            {
                try
                {
                    using var client = listener.Accept();
                    client.ReceiveTimeout = 1000;
                    int read = client.Receive(buffer);
                    string text = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    if (text.Length > 0) Dispatcher.UIThread.Post(() => received(text));
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }) { IsBackground = true, Name = "Easy Notch instance listener" }.Start();
    }
}
