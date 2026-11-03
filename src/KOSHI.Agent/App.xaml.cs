using System.Windows;
using KOSHI.Agent.Core;
using KOSHI.Agent.Core.Windows;
using KOSHI.Agent.Remote;
using Microsoft.Extensions.Logging;
using Serilog;
using Forms = System.Windows.Forms;

namespace KOSHI.Agent;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutex = @"Local\KOSHI.Agent.Tray";
    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private StatusWindow? _window;
    private ChatWindow? _chatWindow;
    private AgentRuntime? _runtime;
    private ILoggerFactory? _loggers;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        // Logs contain technical events only (no window titles, no keystrokes).
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(System.IO.Path.Combine(AgentPaths.UserLogDir, "agent-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        _loggers = LoggerFactory.Create(b => b.AddSerilog(Log.Logger, dispose: false));
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };

        _window = new StatusWindow();
        _chatWindow = new ChatWindow();
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "KOSHI — korporativ monitoring agenti",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowStatus();

        AgentRuntime? runtime = null;
        var host = new WindowsRemoteHost(() => runtime?.RefreshPolicyNowAsync() ?? Task.CompletedTask);
        runtime = new AgentRuntime(new WindowsActivitySource(), host, new AppLockHost(), _loggers.CreateLogger("KOSHI.Agent"));
        _runtime = runtime;
        _runtime.StatusChanged += s => Dispatcher.BeginInvoke(() => Render(s));
        _runtime.ChatReceived += m => Dispatcher.BeginInvoke(() =>
        {
            _window?.AddChatMessage(m);
            _chatWindow?.AddChatMessage(m);
        });
        _runtime.AdminTyping += name => Dispatcher.BeginInvoke(() =>
        {
            _window?.ShowAdminTyping(name);
            _chatWindow?.ShowAdminTyping(name);
        });

        _window.ChatSendRequested = text => _runtime!.SendChatAsync(text);
        _window.TypingTriggered = () => _runtime!.NotifyTyping();
        _window.OpenChatWindowRequested = ShowChatWindow;

        _chatWindow.ChatSendRequested = text => _runtime!.SendChatAsync(text);
        _chatWindow.TypingTriggered = () => _runtime!.NotifyTyping();

        Render(_runtime.Status);
        _runtime.Start();

        if (AgentStore.LoadConfig() is null || e.Args.Contains("--show")) ShowStatus();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_runtime is not null) await _runtime.DisposeAsync();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _loggers?.Dispose();
        await Log.CloseAndFlushAsync();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void Render(AgentStatus s)
    {
        _window?.Render(s);
        if (_tray is null) return;
        var text = s.State switch
        {
            AgentState.Online => "KOSHI — monitoring faol",
            AgentState.Offline => "KOSHI — server bilan aloqa yo'q",
            AgentState.Blocked => "KOSHI — qurilma bloklangan",
            AgentState.Revoked => "KOSHI — kirish bekor qilingan",
            AgentState.Connecting => "KOSHI — ulanmoqda",
            _ => "KOSHI — aktivatsiya qilinmagan",
        };
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("KOSHI holati", null, (_, _) => ShowStatus());
        menu.Items.Add("Administrator bilan chat", null, (_, _) => ShowChatWindow());
        menu.Items.Add("Monitoring siyosati haqida", null, (_, _) => ShowStatus(StatusWindow.Section.Policy));
        return menu;
    }

    private void ShowChatWindow()
    {
        if (_chatWindow is null) return;
        if (_runtime is not null) _chatWindow.LoadHistory(_runtime.ChatHistory);
        _chatWindow.Show();
        _chatWindow.WindowState = WindowState.Normal;
        _chatWindow.Activate();
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
