using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KOSHI.Agent.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace KOSHI.Agent;

public partial class StatusWindow : Window
{
    public enum Section { Status, Policy, Support }

    private readonly Dictionary<Guid, (Border Bubble, TextBlock Body, TextBlock Meta, DateTime CreatedAt, bool FromAdmin)> _items = [];
    private readonly DispatcherTimer _typingTimer;
    private AvailableUpdateInfo? _availableUpdate;

    public Func<string, Task<bool>>? ChatSendRequested { get; set; }
    public Action? TypingTriggered { get; set; }
    public Action? OpenChatWindowRequested { get; set; }
    public Action<AvailableUpdateInfo>? ShowUpdatePromptRequested { get; set; }
    public Func<Task>? CheckUpdateRequested { get; set; }
    public Action? WindowActivatedForRead { get; set; }

    public StatusWindow()
    {
        InitializeComponent();
        VersionText.Text = AgentInfo.VersionWithDate;
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            TypingText.Visibility = Visibility.Collapsed;
        };
        Activated += (_, _) => WindowActivatedForRead?.Invoke();
    }

    public void ShowAvailableUpdate(AvailableUpdateInfo info)
    {
        _availableUpdate = info;
        UpdateBannerTitle.Text = $"Yangi versiya mavjud: v{info.Version}";
        UpdateBannerSub.Text = $"Chiqarilgan sana: {info.BuildDate} · Yangilash uchun bosing";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void OnUpdateBannerClick(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null) ShowUpdatePromptRequested?.Invoke(_availableUpdate);
    }

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (CheckUpdateRequested is null) return;
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = "Tekshirilmoqda…";
        await CheckUpdateRequested();
        CheckUpdateBtn.IsEnabled = true;
        CheckUpdateBtn.Content = "Tekshirish";
        if (_availableUpdate is not null)
        {
            ShowUpdatePromptRequested?.Invoke(_availableUpdate);
        }
        else
        {
            AutoUpdateText.Text = "Eng so‘nggi versiya o‘rnatilgan";
            AutoUpdateText.Foreground = Brush("#047857");
        }
    }

    public void AddChatMessage(ChatMessage m)
    {
        if (_items.ContainsKey(m.Id))
        {
            UpdateChatMessage(m);
            return;
        }
        if (m.FromAdmin)
        {
            _typingTimer.Stop();
            TypingText.Visibility = Visibility.Collapsed;
        }

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6),
            MaxWidth = 300,
            HorizontalAlignment = m.FromAdmin ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = Brush(m.FromAdmin ? "#EEF1FF" : "#4F46E5"),
        };
        var panel = new StackPanel();
        if (m.FromAdmin && !string.IsNullOrWhiteSpace(m.SenderName))
            panel.Children.Add(new TextBlock { Text = m.SenderName, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4338CA") });
        var body = new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush(m.FromAdmin ? "#0B1226" : "#FFFFFF") };
        var meta = new TextBlock { Text = FormatMeta(m.CreatedAt, m.FromAdmin, m.IsRead), FontSize = 9, Foreground = Brush(m.FromAdmin ? "#94A3B8" : "#C7D2FE"), HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(body);
        panel.Children.Add(meta);
        bubble.Child = panel;
        ChatList.Children.Add(bubble);
        _items[m.Id] = (bubble, body, meta, m.CreatedAt, m.FromAdmin);
        ChatScroll.ScrollToEnd();
    }

    public void UpdateChatMessage(ChatMessage m)
    {
        if (!_items.TryGetValue(m.Id, out var entry)) return;
        entry.Body.Text = m.Text;
        entry.Meta.Text = FormatMeta(entry.CreatedAt, entry.FromAdmin, m.IsRead);
    }

    public void DeleteChatMessage(Guid id)
    {
        if (!_items.Remove(id, out var entry)) return;
        ChatList.Children.Remove(entry.Bubble);
    }

    public void MarkMessagesRead(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            if (_items.TryGetValue(id, out var entry) && !entry.FromAdmin)
                entry.Meta.Text = FormatMeta(entry.CreatedAt, false, true);
        }
    }

    public void ClearChatMessages()
    {
        _items.Clear();
        ChatList.Children.Clear();
    }

    private static string FormatMeta(DateTime createdAt, bool fromAdmin, bool isRead)
    {
        var time = createdAt.ToLocalTime().ToString("HH:mm");
        return fromAdmin ? time : $"{time} {(isRead ? "✓✓" : "✓")}";
    }

    public void ShowAdminTyping(string? senderName)
    {
        TypingText.Text = string.IsNullOrWhiteSpace(senderName) ? "Administrator yozmoqda…" : $"{senderName} yozmoqda…";
        TypingText.Visibility = Visibility.Visible;
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private void OnOpenChatWindow(object sender, RoutedEventArgs e) => OpenChatWindowRequested?.Invoke();

    private void OnChatTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ChatInput.Text)) TypingTriggered?.Invoke();
    }

    private async void OnChatSend(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void OnChatKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SendChatAsync(); }
    }

    private async Task SendChatAsync()
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0 || ChatSendRequested is null) return;
        ChatInput.Clear();
        ChatInput.Focus();
        await ChatSendRequested(text);
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
        if (_availableUpdate is null)
        {
            AutoUpdateText.Text = s.AutoUpdateEnabled ? "Bildirishnoma yoqilgan" : "Faqat qo‘lda";
            AutoUpdateText.Foreground = Brush(s.AutoUpdateEnabled ? "#047857" : "#64748B");
        }
        NoticeText.Text = s.MonitoringNotice;
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
