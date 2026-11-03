using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace KOSHI.Agent;

/// <summary>Collects server URL + code and runs the elevated service binary, which owns the machine-wide credential.</summary>
public partial class ActivationWindow : Window
{
    public ActivationWindow() => InitializeComponent();

    public static string? ServiceExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "..", "service", "KOSHI.Agent.Service.exe"),
            Path.Combine(baseDir, "KOSHI.Agent.Service.exe"),
        ];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private async void OnActivate(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var server = ServerBox.Text.Trim();
        var code = CodeBox.Text.Trim();
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            ErrorText.Text = "Server manzili https:// bilan boshlanishi kerak.";
            return;
        }
        if (code.Length < 8)
        {
            ErrorText.Text = "Aktivatsiya kodini to'liq kiriting.";
            return;
        }
        var exe = ServiceExecutable();
        if (exe is null)
        {
            ErrorText.Text = "KOSHI Agent Service topilmadi. Agentni installer orqali qayta o'rnating.";
            return;
        }

        OkButton.IsEnabled = false;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            psi.ArgumentList.Add("activate");
            psi.ArgumentList.Add("--server");
            psi.ArgumentList.Add(server);
            psi.ArgumentList.Add("--code");
            psi.ArgumentList.Add(code);
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            if (p.ExitCode == 0)
            {
                System.Windows.MessageBox.Show(this, "Qurilma muvaffaqiyatli aktivatsiya qilindi. Monitoring bir necha soniyada boshlanadi.", "KOSHI", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                ErrorText.Text = p.ExitCode == 3
                    ? "Aktivatsiya kodi noto'g'ri, muddati o'tgan yoki ishlatilgan."
                    : $"Aktivatsiya bajarilmadi (kod {p.ExitCode}). Server manzili va internetni tekshiring.";
            }
        }
        catch (Win32Exception)
        {
            ErrorText.Text = "Administrator huquqi berilmadi (UAC rad etildi).";
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }
}
