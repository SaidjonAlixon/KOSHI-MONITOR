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
    private readonly HashSet<Guid> _handled = [];
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
                    var next = pending.FirstOrDefault(p => !_handled.Contains(p.Id));
                    if (next is not null)
                    {
                        _handled.Add(next.Id);
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
                "Bu kompyuterga yangi dastur o‘rnatish taqiqlangan. O‘rnatishni davom ettirish uchun administrator parolini kiriting. " +
                "Parol kiritilmasa yoki bekor qilinsa, o‘rnatish to‘xtatiladi.",
                "O‘rnatishga ruxsat",
                password =>
                {
                    var r = Task.Run(() => GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpApprove, Id = install.Id, Password = password }, PipeTimeout))
                        .GetAwaiter().GetResult();
                    return r is null ? (false, false, "KOSHI xizmati bilan aloqa yo‘q, qayta urinib ko‘ring") : (r.Ok, r.Closed, r.Message);
                },
                timeoutSeconds: 150);
            if (dialog.ShowDialog() != true)
                _ = GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpDeny, Id = install.Id }, PipeTimeout);
        }
        finally
        {
            _showing = false;
        }
    }
}
