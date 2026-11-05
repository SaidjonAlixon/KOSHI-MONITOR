using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace KOSHI.Agent.Core;

/// <summary>
/// Downloads and launches the official KOSHI-Agent-Setup.exe. Our own updater must never be frozen,
/// password-gated or blocked by a file lock (Defender / a previous hung setup).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentSelfUpdate
{
    public const string SetupPrefix = "KOSHI-Agent-Setup";
    public const string SilentArgs = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS";

    /// <summary>
    /// Process.ProcessName can be truncated to 15 chars on Windows (EPROCESS.ImageFileName),
    /// so "KOSHI-Agent-Setup-…" becomes "KOSHI-Agent-Set" and a 17-char prefix check would miss it.
    /// </summary>
    public static bool IsSetupProcess(string processName) =>
        processName.StartsWith("KOSHI-Agent-S", StringComparison.OrdinalIgnoreCase);

    public static bool IsSetupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var file = Path.GetFileName(path);
        return file.StartsWith("KOSHI-Agent-S", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSetup(string processName, string? path) =>
        IsSetupProcess(processName) || IsSetupPath(path);

    public static string NewTempPath(string version) =>
        Path.Combine(Path.GetTempPath(), $"{SetupPrefix}-{version}-{Guid.NewGuid():N}.exe");

    /// <summary>Kills hung previous setup processes so the new download can replace the temp file.</summary>
    public static void ReleaseStale(string version)
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (IsSetupProcess(p.ProcessName))
                    p.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone or access denied
            }
            finally
            {
                p.Dispose();
            }
        }

        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), $"{SetupPrefix}-{version}*.exe"))
            {
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
    }

    /// <summary>Download to a unique temp file, retrying on file-lock. Returns the path that was written.</summary>
    public static async Task<string> DownloadToTempAsync(
        HttpClient http, string version, string? fallbackFileName, long expectedSize, IProgress<int>? progress, CancellationToken ct)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0) ReleaseStale(version);
            var dest = NewTempPath(version);
            try
            {
                await DownloadAsync(http, dest, fallbackFileName, expectedSize, progress, ct);
                return dest;
            }
            catch (IOException ex)
            {
                last = ex;
                try { File.Delete(dest); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                await Task.Delay(400 * (attempt + 1), ct);
            }
        }
        throw last ?? new IOException("Yangilanish faylini yozib bo‘lmadi — u boshqa jarayon tomonidan band.");
    }

    public static async Task DownloadAsync(HttpClient http, string dest, string? fallbackFileName, long expectedSize, IProgress<int>? progress, CancellationToken ct)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fallbackFileName) ? dest : fallbackFileName);
        string[] urls =
        [
            "api/agent/release/download",
            "downloads/KOSHI-Agent-Setup.exe",
            string.IsNullOrWhiteSpace(name) ? "" : "downloads/" + Uri.EscapeDataString(name),
        ];
        HttpResponseMessage? resp = null;
        var lastCode = 0;
        foreach (var url in urls.Where(u => u.Length > 0))
        {
            resp?.Dispose();
            resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            lastCode = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode) break;
        }
        if (resp is null || !resp.IsSuccessStatusCode)
        {
            resp?.Dispose();
            throw new HttpRequestException($"Yuklab bo‘lmadi (HTTP {lastCode}).");
        }

        using (resp)
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? expectedSize;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                readTotal += read;
                if (total > 0) progress?.Report((int)Math.Clamp(readTotal * 100 / total, 1, 99));
            }
            await fs.FlushAsync(ct);
        }
    }

    public static async Task<bool> ShaMatchesAsync(string path, string? expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        IOException? last = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException ex)
            {
                last = ex;
                await Task.Delay(300, ct);
            }
        }
        throw last ?? new IOException("Yuklangan o‘rnatuvchini o‘qib bo‘lmadi.");
    }

    /// <summary>Copies the downloaded setup next to a canonical name the guards whitelist.</summary>
    public static string Stage(string path)
    {
        WaitUntilReadable(path);
        string[] dirs =
        [
            Path.Combine(AgentPaths.MachineDir, "update"),
            Path.Combine(Path.GetTempPath(), $"KOSHI-upd-{Guid.NewGuid():N}"),
        ];
        IOException? last = null;
        foreach (var dir in dirs)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, $"{SetupPrefix}.exe");
                File.Copy(path, dest, overwrite: true);
                return dest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex as IOException ?? new IOException(ex.Message, ex);
            }
        }
        throw last ?? new IOException("O‘rnatuvchini saqlab bo‘lmadi.");
    }

    /// <param name="alreadyElevated">
    /// True when called from the LocalSystem service — no UAC, no Explorer.
    /// False from the tray: ShellExecute runas (UAC). Never launch via a .lnk.
    /// </param>
    public static void Launch(string path, bool alreadyElevated = false)
    {
        var staged = Stage(path);
        IOException? last = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                ProcessStartInfo psi = alreadyElevated
                    ? new ProcessStartInfo(staged, SilentArgs)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(staged)!,
                    }
                    : new ProcessStartInfo(staged, SilentArgs)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = Path.GetDirectoryName(staged)!,
                    };
                using var started = Process.Start(psi) ?? throw new IOException("O‘rnatuvchi ishga tushmadi.");
                if (started.HasExited && started.ExitCode != 0)
                    throw new IOException($"O‘rnatuvchi darhol yopildi (kod {started.ExitCode}).");
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(250);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                    throw new IOException("Yangilash bekor qilindi. Windows ruxsat oynasida Ha ni bosing.");
                last = new IOException(ex.Message, ex);
                if (i >= 9) break;
                Thread.Sleep(250);
            }
        }

        throw last ?? new IOException("O‘rnatuvchini ishga tushirib bo‘lmadi.");
    }

    private static void WaitUntilReadable(string path)
    {
        for (var i = 0; i < 25; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 0) return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
