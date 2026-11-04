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
    public const string SilentArgs = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES";

    public static bool IsSetupProcess(string processName) =>
        processName.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase);

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

    public static async Task DownloadAsync(HttpClient http, string dest, string? fallbackFileName, long expectedSize, IProgress<int>? progress, CancellationToken ct)
    {
        var resp = await http.GetAsync("api/agent/release/download", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            resp.Dispose();
            var name = Path.GetFileName(string.IsNullOrWhiteSpace(fallbackFileName) ? dest : fallbackFileName);
            resp = await http.GetAsync("downloads/" + Uri.EscapeDataString(name), HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                resp.Dispose();
                throw new HttpRequestException($"Yuklab bo‘lmadi (HTTP {code}).");
            }
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

    public static void Launch(string path)
    {
        IOException? last = null;
        for (var i = 0; i < 12; i++)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path, SilentArgs) { UseShellExecute = true });
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(250);
            }
        }

        var copy = Path.Combine(Path.GetTempPath(), $"{SetupPrefix}-run-{Guid.NewGuid():N}.exe");
        try
        {
            File.Copy(path, copy, overwrite: true);
            Process.Start(new ProcessStartInfo(copy, SilentArgs) { UseShellExecute = true });
        }
        catch (Exception)
        {
            throw last ?? new IOException("O‘rnatuvchini ishga tushirib bo‘lmadi.");
        }
    }
}
