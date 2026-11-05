using System.Windows;
using KOSHI.Agent.Core;

namespace KOSHI.Agent;

public partial class UpdatePromptWindow : Window
{
    private readonly AvailableUpdateInfo _info;
    private readonly AgentRuntime _runtime;

    public UpdatePromptWindow(AvailableUpdateInfo info, AgentRuntime runtime)
    {
        InitializeComponent();
        _info = info;
        _runtime = runtime;
        VersionInfoText.Text = $"Yangi versiya: v{info.Version} ({info.BuildDate}) — joriy: v{AgentInfo.Version}";
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        LaterBtn.IsEnabled = false;
        ErrorLabel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusLabel.Text = "Yangilanish yuklanmoqda… 0%";

        var progress = new Progress<int>(pct =>
        {
            DownloadBar.Value = pct;
            StatusLabel.Text = pct < 100 ? $"Yangilanish yuklanmoqda… {pct}%" : "O‘rnatilmoqda… Dastur o‘zi qayta ishga tushadi.";
        });

        var (ok, msg) = await _runtime.InstallUpdateAsync(_info, progress);
        if (!ok)
        {
            ErrorLabel.Text = msg;
            ErrorLabel.Visibility = Visibility.Visible;
            UpdateBtn.IsEnabled = true;
            LaterBtn.IsEnabled = true;
            return;
        }

        StatusLabel.Text = msg;
        await Task.Delay(2500);
    }

    private void OnLater(object sender, RoutedEventArgs e) => Close();
}
