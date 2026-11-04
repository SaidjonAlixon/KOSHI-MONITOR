using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KOSHI.Agent.Remote;

public partial class AppLockWindow : Window
{
    private readonly Func<string, (bool Ok, bool Closed, string? Message)> _verify;
    private readonly DispatcherTimer _timeoutTimer;
    private int _attempts;
    private bool _syncing;

    private string CurrentPassword => PasswordPlain.Visibility == Visibility.Visible ? PasswordPlain.Text : PasswordInput.Password;

    public AppLockWindow(string ruleName, string processName, Func<string, bool> verify)
        : this(string.IsNullOrWhiteSpace(ruleName) ? processName : ruleName, null, null, null, p => (verify(p), false, null))
    {
    }

    /// <param name="verify">Returns whether the password is accepted, whether the request is gone (close the dialog) and an optional error text.</param>
    public AppLockWindow(string title, string? subtitle, string? description, string? okText,
        Func<string, (bool Ok, bool Closed, string? Message)> verify, int timeoutSeconds = 45)
    {
        InitializeComponent();
        _verify = verify;
        TitleText.Text = title;
        if (subtitle is not null) SubtitleText.Text = subtitle;
        if (description is not null) DescriptionText.Text = description;
        if (okText is not null) OkButton.Content = okText;

        // Auto-cancel (the locked app / installer is closed) if no password is entered in time
        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
        _timeoutTimer.Tick += (_, _) =>
        {
            _timeoutTimer.Stop();
            if (IsLoaded) DialogResult = false;
        };

        Loaded += (_, _) =>
        {
            _timeoutTimer.Start();
            Activate();
            PasswordInput.Focus();
        };
        Closed += (_, _) => _timeoutTimer.Stop();
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
        var (ok, closed, message) = _verify(CurrentPassword);
        if (ok)
        {
            _timeoutTimer.Stop();
            DialogResult = true;
            return;
        }
        if (closed)
        {
            _timeoutTimer.Stop();
            DialogResult = false;
            return;
        }
        _attempts++;
        ErrorText.Text = message ?? $"Parol noto‘g‘ri! Urinish: {_attempts}/3";
        ErrorText.Visibility = Visibility.Visible;
        _syncing = true;
        PasswordInput.Clear();
        PasswordPlain.Clear();
        _syncing = false;
        if (ShowPwd.IsChecked == true) PasswordPlain.Focus(); else PasswordInput.Focus();
        if (_attempts >= 3)
        {
            _timeoutTimer.Stop();
            DialogResult = false;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _timeoutTimer.Stop();
        DialogResult = false;
    }
}
