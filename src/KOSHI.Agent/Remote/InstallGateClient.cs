using System.IO;
using KOSHI.Agent.Core;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace KOSHI.Agent.Remote;

/// <summary>
/// Tray side of the installation lock: polls the SYSTEM service for installers it has frozen in this session and asks
/// for the protection password. The service verifies the password and resumes or closes the installer.
/// </summary>
public sealed class InstallGateClient(ILogger logger)
{
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(3);
    private readonly Dictionary<Guid, DateTime> _dismissed = [];
    private bool _showing;

    public void Start(CancellationToken ct) => _ = Task.Run(() => LoopAsync(ct), ct);

    /// <summary>Forwards the server's "refresh-policy" push so web/VPN/install rules apply without waiting for the service poll.</summary>
    public void RequestServiceRefresh() => _ = GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpRefresh }, PipeTimeout);

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var res = await GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpPending }, PipeTimeout, ct);
                if (res?.Pending is { Count: > 0 } pending && !_showing)
                {
                    var cutoff = DateTime.UtcNow;
                    foreach (var gone in _dismissed.Where(kv => kv.Value <= cutoff).Select(kv => kv.Key).ToList())
                        _dismissed.Remove(gone);
                    var next = pending.FirstOrDefault(p => !_dismissed.ContainsKey(p.Id));
                    if (next is not null)
                    {
                        _showing = true;
                        _ = Application.Current?.Dispatcher.BeginInvoke(() => Prompt(next));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Install gate poll failed");
            }
            await Task.Delay(1000, ct);
        }
    }

    private void Prompt(GatePendingInstall install)
    {
        try
        {
            var program = install.Description ?? (install.Path is { } p ? Path.GetFileName(p) : install.Name);
            var dialog = new AppLockWindow(
                $"Yangi dastur o‘rnatish: {program}",
                "Dastur o‘rnatish administrator paroli bilan cheklangan",
                "Bu kompyuterga yangi dastur o‘rnatish taqiqlangan. Faqat shu o‘rnatish uchun administrator parolini kiriting. " +
                "Bekor qilinsa yoki keyin boshqa (yoki shu) dasturni qayta o‘rnatmoqchi bo‘lsangiz, parol qayta so‘raladi.",
                "O‘rnatishga ruxsat",
                password =>
                {
                    var r = Task.Run(() => GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpApprove, Id = install.Id, Password = password }, PipeTimeout))
                        .GetAwaiter().GetResult();
                    return r is null ? (false, false, "KOSHI xizmati bilan aloqa yo‘q, qayta urinib ko‘ring") : (r.Ok, r.Closed, r.Message);
                },
                timeoutSeconds: 150);
            if (dialog.ShowDialog() != true)
            {
                _dismissed[install.Id] = DateTime.UtcNow.AddSeconds(8);
                _ = GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpDeny, Id = install.Id }, PipeTimeout);
            }
        }
        finally
        {
            _showing = false;
        }
    }
}
