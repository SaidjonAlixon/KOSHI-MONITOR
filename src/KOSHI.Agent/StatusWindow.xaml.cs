using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace KOSHI.Agent;

public partial class StatusWindow : Window
{
    public enum Section { Status, Policy, Support }

    public StatusWindow()
    {
        InitializeComponent();
        VersionText.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
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

    /// <summary>Closing the window only hides it; the tray indicator stays visible while the agent runs.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
