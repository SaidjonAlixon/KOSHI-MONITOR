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
        Message.Text = $"Administrator {adminName} bu kompyuterni masofadan ko'rmoqda va boshqarmoqda";
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top;
        };
    }

    private void OnEnd(object sender, RoutedEventArgs e) => _end();
}
