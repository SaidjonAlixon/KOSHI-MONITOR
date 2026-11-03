using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KOSHI.Agent.Core;

public sealed record AgentConfig(string ServerUrl, Guid DeviceId, string DisplayName, DateTime ActivatedAt);

public static class AgentPaths
{
    /// <summary>Machine-wide state written by the elevated service during activation (ACL: SYSTEM/Admins full, Users read).</summary>
    public static string MachineDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KOSHI");

    public static string ConfigFile => Path.Combine(MachineDir, "agent.json");

    public static string CredentialFile => Path.Combine(MachineDir, "device.key");

    /// <summary>Per-user state of the tray agent (offline queue, logs).</summary>
    public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOSHI");

    public static string QueueDir => Path.Combine(UserDir, "queue");

    public static string UserLogDir => Path.Combine(UserDir, "logs");

    public static string MachineLogDir => Path.Combine(MachineDir, "logs");
}

/// <summary>
/// Stores the agent configuration and the device credential. The credential is protected with DPAPI
/// (LocalMachine scope + KOSHI-specific entropy) so it is unusable when copied to another computer.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KOSHI.Agent.DeviceCredential.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AgentConfig? LoadConfig()
    {
        try
        {
            return File.Exists(AgentPaths.ConfigFile)
                ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(AgentPaths.ConfigFile), Json)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? LoadCredential()
    {
        try
        {
            if (!File.Exists(AgentPaths.CredentialFile)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(AgentPaths.CredentialFile), Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Save(AgentConfig config, string credential)
    {
        Directory.CreateDirectory(AgentPaths.MachineDir);
        WriteAtomic(AgentPaths.ConfigFile, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, Json)));
        WriteAtomic(AgentPaths.CredentialFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(credential), Entropy, DataProtectionScope.LocalMachine));
    }

    public static void Delete()
    {
        foreach (var f in new[] { AgentPaths.ConfigFile, AgentPaths.CredentialFile })
            if (File.Exists(f)) File.Delete(f);
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
