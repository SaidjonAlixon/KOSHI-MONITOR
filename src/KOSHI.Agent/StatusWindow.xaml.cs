using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using KOSHI.Agent.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace KOSHI.Agent;

public partial class StatusWindow : Window
{
    public enum Section { Status, Policy, Support }

    public StatusWindow()
    {
        InitializeComponent();
        VersionText.Text = AgentInfo.Version;
    }

    public void Render(AgentStatus s)
    {
        var (title, detail, bg, fg) = s.State switch
        {
            AgentState.Online => ("Monitoring faol — serverga ulangan", "Faol dastur va faol/bo'sh vaqt tashkilot siyosatiga muvofiq yuborilmoqda.", "#ECFDF5", "#065F46"),
            AgentState.Connecting => ("Serverga ulanmoqda…", "Ma'lumotlar vaqtincha kompyuterda saqlanadi.", "#EFF6FF", "#1E40AF"),
            AgentState.Offline => ("Server bilan aloqa yo'q", $"Ma'lumotlar lokal navbatda saqlanmoqda ({s.QueueLength} paket) va aloqa tiklanganda yuboriladi.", "#FFFBEB", "#92400E"),
            AgentState.Blocked => ("Qurilma administrator tomonidan bloklangan", "Monitoring to'xtatilgan, ma'lumot yuborilmaydi.", "#FEF2F2", "#991B1B"),
            AgentState.Revoked => ("Qurilma kirish huquqi bekor qilingan", "Qayta ulash uchun IT bo'limidan yangi aktivatsiya kodi oling.", "#FEF2F2", "#991B1B"),
            _ => ("Qurilma aktivatsiya qilinmagan", "Monitoring ishlamayapti. IT bo'limidan bir martalik aktivatsiya kodini oling.", "#FFF7ED", "#9A3412"),
        };
        StatusText.Text = title;
        StatusDetail.Text = detail;
        StatusBox.Background = Brush(bg);
        StatusBox.BorderBrush = Brush(fg, 0x40);
        StatusText.Foreground = StatusDetail.Foreground = Brush(fg);

        ServerText.Text = s.ServerUrl ?? "sozlanmagan";
        DeviceText.Text = s.DeviceName ?? "—";
        MonitoringText.Text = s.MonitoringActive ? "Yoqilgan" : "To'xtatilgan";
        PolicyText.Text = s.PolicyName is null ? "standart" : $"{s.PolicyName} (v{s.PolicyVersion})";
        SyncText.Text = s.LastSync?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "—";
        NoticeText.Text = s.MonitoringNotice;
        SupportText.Text = s.SupportContact;
        ActivateButton.Visibility = s.State is AgentState.NotActivated or AgentState.Revoked ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Focus(Section section)
    {
        FrameworkElement target = section switch
        {
            Section.Policy => PolicySection,
            Section.Support => SupportSection,
            _ => StatusSection,
        };
        target.BringIntoView();
    }

    private void OnActivate(object sender, RoutedEventArgs e) => new ActivationWindow { Owner = this }.ShowDialog();

    /// <summary>Closing the window only hides it; the tray indicator stays visible while the agent runs.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private static SolidColorBrush Brush(string hex, byte alpha = 0xFF)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }
}
