using System.Windows;
using Forms = System.Windows.Forms;

namespace KOSHI.Agent;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutex = @"Local\KOSHI.Agent.Tray";
    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private StatusWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _window = new StatusWindow();
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "KOSHI — korporativ monitoring agenti",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowStatus();
        ShowStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("KOSHI holati", null, (_, _) => ShowStatus());
        menu.Items.Add("Monitoring siyosati haqida", null, (_, _) => ShowStatus(StatusWindow.Section.Policy));
        menu.Items.Add("Texnik yordam", null, (_, _) => ShowStatus(StatusWindow.Section.Support));
        return menu;
    }

    private void ShowStatus(StatusWindow.Section section = StatusWindow.Section.Status)
    {
        if (_window is null) return;
        _window.Focus(section);
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/koshi.ico"))
                   ?? throw new InvalidOperationException("Tray icon resource is missing.");
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
