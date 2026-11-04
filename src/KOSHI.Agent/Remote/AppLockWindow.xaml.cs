using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KOSHI.Agent.Remote;

public partial class AppLockWindow : Window
{
    private readonly Func<string, bool> _verify;
    private readonly DispatcherTimer _timeoutTimer;
    private int _attempts;
    private bool _syncing;

    private string CurrentPassword => PasswordPlain.Visibility == Visibility.Visible ? PasswordPlain.Text : PasswordInput.Password;

    public AppLockWindow(string ruleName, string processName, Func<string, bool> verify)
    {
        InitializeComponent();
        _verify = verify;
        TitleText.Text = string.IsNullOrWhiteSpace(ruleName) ? processName : ruleName;

        // Auto-cancel and kill the locked app if no password is entered within 45 seconds
        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
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
        if (_verify(CurrentPassword))
        {
            _timeoutTimer.Stop();
            DialogResult = true;
            return;
        }
        _attempts++;
        ErrorText.Text = $"Parol noto‘g‘ri! Urinish: {_attempts}/3";
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
