using System.Diagnostics;
using System.Security.Principal;
using KOSHI.Agent.Core;
using KOSHI.Contracts;

namespace KOSHI.Agent.Service;

/// <summary>
/// Elevated command-line entry points used by the installer and the tray app:
///   activate --server https://koshi.uz --code XXXX-XXXX-XXXX
///   deactivate   (AppLocker rollback + local credential removal; used on uninstall)
/// Exit codes: 0 ok, 2 usage, 3 activation rejected, 4 network/server error, 5 not elevated.
/// </summary>
public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!IsElevated())
        {
            Console.Error.WriteLine("Administrator privileges are required.");
            return 5;
        }

        if (args[0] == "deactivate")
        {
            if (!UpgradeGate.IsOpen)
            {
                Console.Error.WriteLine("Deactivation is disabled.");
                return 5;
            }
            var rollback = AppLockerAdapter.Rollback();
            AgentStore.Delete();
            Console.WriteLine($"Deactivated. AppLocker: {rollback.Message}");
            return 0;
        }

        var server = Arg(args, "--server");
        var code = Arg(args, "--code");
        if (server is null || code is null)
        {
            Console.Error.WriteLine("Usage: KOSHI.Agent.Service.exe activate --server https://koshi.uz --code XXXX-XXXX-XXXX");
            return 2;
        }

        try
        {
            PrepareMachineDirectory();
            using var api = new KoshiApiClient(server, credential: null);
            var res = await api.ActivateAsync(new DeviceActivationRequest
            {
                ActivationCode = code,
                Hostname = Environment.MachineName,
                OsVersion = WindowsEdition.Describe(),
                AgentVersion = AgentInfo.Version,
            }, CancellationToken.None);
            AgentStore.Save(new AgentConfig(api.BaseAddress.ToString().TrimEnd('/'), res.DeviceId, res.DisplayName, DateTime.UtcNow), res.DeviceCredential);
            Console.WriteLine($"Activated as '{res.DisplayName}' ({res.DeviceId}).");
            return 0;
        }
        catch (DeviceRejectedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or UriFormatException)
        {
            Console.Error.WriteLine($"Server error: {ex.Message}");
            return 4;
        }
    }

    /// <summary>ProgramData\KOSHI: SYSTEM and Administrators full control, Users read-only (the tray agent reads the protected credential).</summary>
    private static void PrepareMachineDirectory()
    {
        Directory.CreateDirectory(AgentPaths.MachineDir);
        Directory.CreateDirectory(AgentPaths.MachineLogDir);
        using var p = Process.Start(new ProcessStartInfo("icacls.exe")
        {
            ArgumentList = { AgentPaths.MachineDir, "/inheritance:r", "/grant:r", "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-32-545:(OI)(CI)RX" },
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        p.WaitForExit();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
