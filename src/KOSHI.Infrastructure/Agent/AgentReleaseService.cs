using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.Extensions.Logging;

namespace KOSHI.Infrastructure.Agent;

public sealed class AgentReleaseOptions
{
    public const string Section = "AgentReleases";

    /// <summary>Directory where uploaded installers and latest.json live (must be writable by the API user).</summary>
    public string Path { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "releases");

    /// <summary>Optional installer imported as the initial release when the store is empty.</summary>
    public string? SeedInstaller { get; set; }

    public long MaxUploadBytes { get; set; } = 260L * 1024 * 1024;
}

/// <summary>
/// Stores the current KOSHI Agent installer and its metadata on disk. Administrators upload new versions
/// from the admin panel; agents poll the metadata and auto-update. Files are served by the API, so no
/// server redeploy or filesystem juggling is needed to publish an update.
/// </summary>
public sealed partial class AgentReleaseService(
    AgentReleaseOptions options,
    AuditWriter audit,
    IKoshiDbContext db,
    TimeProvider clock,
    ILogger<AgentReleaseService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string MetadataPath => Path.Combine(options.Path, "latest.json");

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionRegex();

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.Path);
        if (File.Exists(MetadataPath)) return;
        if (string.IsNullOrWhiteSpace(options.SeedInstaller) || !File.Exists(options.SeedInstaller))
        {
            logger.LogInformation("No agent release present and no seed installer configured.");
            return;
        }
        // Prefer the version from a latest.json shipped next to the seed installer; fall back to the file name.
        var besideMeta = Path.Combine(Path.GetDirectoryName(options.SeedInstaller)!, "latest.json");
        string? version = null;
        if (File.Exists(besideMeta))
        {
            try { version = JsonSerializer.Deserialize<AgentReleaseDto>(await File.ReadAllTextAsync(besideMeta, ct), Json)?.Version; }
            catch (JsonException) { /* fall back below */ }
        }
        version ??= ParseVersionFromName(Path.GetFileName(options.SeedInstaller)) ?? "1.0.0";
        await using var fs = File.OpenRead(options.SeedInstaller);
        await ImportAsync(fs, version, "bundled", actorAudit: false, ct);
        logger.LogInformation("Seeded initial agent release {Version} from {Path}", version, options.SeedInstaller);
    }

    public async Task<AgentReleaseDto?> GetLatestAsync(CancellationToken ct = default)
    {
        if (!File.Exists(MetadataPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentReleaseDto>(await File.ReadAllTextAsync(MetadataPath, ct), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Full path to the current installer, or null if none is published.</summary>
    public async Task<string?> GetInstallerPathAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct);
        if (latest is null) return null;
        var path = Path.Combine(options.Path, SafeName(latest.FileName));
        return File.Exists(path) ? path : null;
    }

    public async Task<AgentReleaseDto> UploadAsync(Stream content, string version, string fileName, CancellationToken ct = default)
    {
        version = version.Trim();
        if (!VersionRegex().IsMatch(version))
            throw Errors.Validation("version", "Versiya X.Y.Z ko‘rinishida bo‘lishi kerak (masalan, 1.0.6).");

        var current = await GetLatestAsync(ct);
        if (current is not null && Version.TryParse(current.Version, out var cur) && Version.TryParse(version, out var next) && next < cur)
            throw Errors.Validation("version", $"Yuklanayotgan versiya ({version}) joriy versiyadan ({current.Version}) eski bo‘lishi mumkin emas.");

        var dto = await ImportAsync(content, version, "uploaded", actorAudit: true, ct);
        logger.LogInformation("Admin published agent release {Version}", version);
        return dto;
    }

    private async Task<AgentReleaseDto> ImportAsync(Stream content, string version, string source, bool actorAudit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(options.Path);
            var fileName = $"KOSHI-Agent-Setup-{version}.exe";
            var target = Path.Combine(options.Path, fileName);
            var tmp = target + ".tmp";

            long size;
            string sha;
            await using (var dest = File.Create(tmp))
            {
                await content.CopyToAsync(dest, ct);
                size = dest.Length;
            }
            if (size is < 1_000_000 or > 300_000_000)
            {
                File.Delete(tmp);
                throw Errors.Validation("file", "Fayl hajmi 1 MB dan 300 MB gacha bo‘lgan Windows EXE bo‘lishi kerak.");
            }
            await using (var verify = File.OpenRead(tmp))
            {
                var header = new byte[2];
                _ = await verify.ReadAsync(header.AsMemory(0, 2), ct);
                if (header[0] != (byte)'M' || header[1] != (byte)'Z')
                {
                    verify.Close();
                    File.Delete(tmp);
                    throw Errors.Validation("file", "Fayl Windows EXE emas.");
                }
                verify.Position = 0;
                sha = Convert.ToHexString(await SHA256.HashDataAsync(verify, ct));
            }

            File.Move(tmp, target, overwrite: true);
            // Remove older installer files, keep the new one.
            foreach (var old in Directory.EnumerateFiles(options.Path, "KOSHI-Agent-Setup-*.exe"))
                if (!string.Equals(Path.GetFileName(old), fileName, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(old); } catch (IOException) { }

            var dto = new AgentReleaseDto(version, clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd"), fileName, size, sha, clock.GetUtcNow().UtcDateTime, source);
            await File.WriteAllTextAsync(MetadataPath, JsonSerializer.Serialize(dto, Json), ct);

            if (actorAudit)
            {
                audit.Record("agent_release.publish", "AgentRelease", version, new { version, size, sha256 = sha });
                await db.SaveChangesAsync(ct);
            }
            return dto;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string SafeName(string name) => Path.GetFileName(name).Replace("..", string.Empty);

    private static string? ParseVersionFromName(string name)
    {
        var m = Regex.Match(name, @"(\d+\.\d+\.\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }
}
