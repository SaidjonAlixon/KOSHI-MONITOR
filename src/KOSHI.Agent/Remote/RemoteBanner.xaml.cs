using System.Windows;

namespace KOSHI.Agent.Remote;

/// <summary>Always-on-top notice shown to the employee for the entire remote session.</summary>
public partial class RemoteBanner : Window
{
    private readonly Action _end;

    public RemoteBanner(string adminName, Action end)
    {
        InitializeComponent();
        _end = end;
        // Keep it short/discreet; the full admin name is available on hover.
        ToolTip = $"Administrator {adminName} bu kompyuterni masofadan ko'rmoqda. Yopish uchun ✕ ni bosing.";
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        };
    }

    private void OnEnd(object sender, RoutedEventArgs e) => _end();
}
