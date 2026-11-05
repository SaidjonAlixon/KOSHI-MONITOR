using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KOSHI.Agent.Remote;

public partial class AppLockWindow : Window
{
    private readonly Func<string, (bool Ok, bool Closed, string? Message)> _verify;
    private readonly DispatcherTimer? _timeoutTimer;
    private readonly bool _mustEnterPassword;
    private int _attempts;
    private bool _syncing;
    private bool _accepted;

    private string CurrentPassword => PasswordPlain.Visibility == Visibility.Visible ? PasswordPlain.Text : PasswordInput.Password;

    public AppLockWindow(string ruleName, string processName, Func<string, bool> verify, bool mustEnterPassword = false)
        : this(string.IsNullOrWhiteSpace(ruleName) ? processName : ruleName, null, null, null, p => (verify(p), false, null),
            timeoutSeconds: mustEnterPassword ? 0 : 45, mustEnterPassword: mustEnterPassword)
    {
    }

    /// <param name="verify">Returns whether the password is accepted, whether the request is gone (close the dialog) and an optional error text.</param>
    /// <param name="mustEnterPassword">When true a wrong password never proceeds. Closing (X) returns to the desktop without unlocking.</param>
    public AppLockWindow(string title, string? subtitle, string? description, string? okText,
        Func<string, (bool Ok, bool Closed, string? Message)> verify, int timeoutSeconds = 45, bool mustEnterPassword = false)
    {
        InitializeComponent();
        _verify = verify;
        _mustEnterPassword = mustEnterPassword;
        TitleText.Text = title;
        if (subtitle is not null) SubtitleText.Text = subtitle;
        if (description is not null) DescriptionText.Text = description;
        if (okText is not null) OkButton.Content = okText;

        if (mustEnterPassword)
        {
            DescriptionText.Text = description ??
                "Sozlamalarni ochish uchun administrator paroli kerak. Noto‘g‘ri parol sozlamalarni ochmaydi. Yopsangiz ish stoliga qaytasiz — ruxsat etilgan ishlar davom etadi.";
            CancelButton.Content = "Yopish";
        }

        if (timeoutSeconds > 0 && !mustEnterPassword)
        {
            _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _timeoutTimer.Tick += (_, _) =>
            {
                _timeoutTimer.Stop();
                Dismiss();
            };
        }

        Loaded += (_, _) =>
        {
            _timeoutTimer?.Start();
            Activate();
            PasswordInput.Focus();
        };
        Closed += (_, _) => _timeoutTimer?.Stop();
        Deactivated += (_, _) =>
        {
            if (IsLoaded && IsVisible)
            {
                Topmost = false;
                Topmost = true;
                Activate();
                PasswordInput.Focus();
            }
        };
    }

    private void OnToggleShow(object sender, RoutedEventArgs e)
    {
        if (ShowPwd.IsChecked == true)
        {
            _syncing = true;
            PasswordPlain.Text = PasswordInput.Password;
            _syncing = false;
            PasswordPlain.Visibility = Visibility.Visible;
            PasswordInput.Visibility = Visibility.Collapsed;
            PasswordPlain.Focus();
            PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
        }
        else
        {
            _syncing = true;
            PasswordInput.Password = PasswordPlain.Text;
            _syncing = false;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordInput.Focus();
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        PasswordPlain.Text = PasswordInput.Password;
        _syncing = false;
    }

    private void OnPlainChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        PasswordInput.Password = PasswordPlain.Text;
        _syncing = false;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            ShowWrong("Parol kiritilmagan. To‘g‘ri parol terilmaguncha davom ettirib bo‘lmaydi.");
            return;
        }

        var (ok, closed, message) = _verify(CurrentPassword);
        if (ok)
        {
            _accepted = true;
            _timeoutTimer?.Stop();
            DialogResult = true;
            return;
        }
        if (closed && !_mustEnterPassword)
        {
            Dismiss();
            return;
        }
        ShowWrong(message ?? $"Parol noto‘g‘ri ({_attempts + 1}). To‘g‘ri parol kiritilmaguncha dastur ochilmaydi.");
        // Wrong password never unlocks. X / Yopish still returns to the desktop.
    }

    private void ShowWrong(string message)
    {
        _attempts++;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        _syncing = true;
        PasswordInput.Clear();
        PasswordPlain.Clear();
        _syncing = false;
        if (ShowPwd.IsChecked == true) PasswordPlain.Focus(); else PasswordInput.Focus();
    }

    private void OnOverlayClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) Dismiss();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Dismiss();

    /// <summary>Closes the overlay without unlocking. The protected app stays blocked; the desktop is usable again.</summary>
    private void Dismiss()
    {
        if (_accepted) return;
        _timeoutTimer?.Stop();
        try { DialogResult = false; }
        catch (InvalidOperationException) { Close(); }
    }
}
