using System.Windows;

namespace KOSHI.Agent.Remote;

public partial class AppLockWindow : Window
{
    private readonly Func<string, bool> _verify;
    private int _attempts;

    public AppLockWindow(string ruleName, string processName, Func<string, bool> verify)
    {
        InitializeComponent();
        _verify = verify;
        TitleText.Text = string.IsNullOrWhiteSpace(ruleName) ? processName : ruleName;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_verify(PasswordInput.Password))
        {
            DialogResult = true;
            return;
        }
        _attempts++;
        ErrorText.Text = $"Parol noto‘g‘ri. Urinish: {_attempts}/3";
        ErrorText.Visibility = Visibility.Visible;
        PasswordInput.Clear();
        PasswordInput.Focus();
        if (_attempts >= 3) DialogResult = false;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
