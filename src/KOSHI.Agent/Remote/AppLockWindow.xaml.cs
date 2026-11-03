using System.Windows;
using System.Windows.Threading;

namespace KOSHI.Agent.Remote;

public partial class AppLockWindow : Window
{
    private readonly Func<string, bool> _verify;
    private readonly DispatcherTimer _timeoutTimer;
    private int _attempts;

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

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_verify(PasswordInput.Password))
        {
            _timeoutTimer.Stop();
            DialogResult = true;
            return;
        }
        _attempts++;
        ErrorText.Text = $"Parol noto‘g‘ri! Urinish: {_attempts}/3";
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Clear();
        PasswordInput.Focus();
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
