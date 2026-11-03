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

public partial class ChatWindow : Window
{
    private readonly HashSet<Guid> _renderedIds = [];
    private readonly DispatcherTimer _typingTimer;

    public Func<string, Task<bool>>? ChatSendRequested { get; set; }
    public Action? TypingTriggered { get; set; }

    public ChatWindow()
    {
        InitializeComponent();
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            TypingText.Visibility = Visibility.Collapsed;
        };
        Loaded += (_, _) => ChatInput.Focus();
    }

    public void LoadHistory(IEnumerable<ChatMessage> messages)
    {
        foreach (var m in messages) AddChatMessage(m);
    }

    public void AddChatMessage(ChatMessage m)
    {
        if (!_renderedIds.Add(m.Id)) return;
        if (m.FromAdmin)
        {
            _typingTimer.Stop();
            TypingText.Visibility = Visibility.Collapsed;
        }

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 320,
            HorizontalAlignment = m.FromAdmin ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = Brush(m.FromAdmin ? "#EEF1FF" : "#4F46E5"),
        };
        var panel = new StackPanel();
        if (m.FromAdmin && !string.IsNullOrWhiteSpace(m.SenderName))
            panel.Children.Add(new TextBlock { Text = m.SenderName, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4338CA"), Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brush(m.FromAdmin ? "#0B1226" : "#FFFFFF") });
        panel.Children.Add(new TextBlock { Text = m.CreatedAt.ToLocalTime().ToString("HH:mm"), FontSize = 9.5, Foreground = Brush(m.FromAdmin ? "#64748B" : "#C7D2FE"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) });
        bubble.Child = panel;
        ChatList.Children.Add(bubble);
        ChatScroll.ScrollToEnd();
    }

    public void ShowAdminTyping(string? senderName)
    {
        TypingText.Text = string.IsNullOrWhiteSpace(senderName) ? "Administrator yozmoqda…" : $"{senderName} yozmoqda…";
        TypingText.Visibility = Visibility.Visible;
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private void OnChatTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ChatInput.Text)) TypingTriggered?.Invoke();
    }

    private async void OnChatSend(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void OnChatKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendChatAsync();
        }
    }

    private async Task SendChatAsync()
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0 || ChatSendRequested is null) return;
        ChatInput.Clear();
        ChatInput.Focus();
        await ChatSendRequested(text);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(c);
    }
}
