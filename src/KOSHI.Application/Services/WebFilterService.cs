using System.Globalization;
using System.Text.RegularExpressions;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

/// <summary>Blocked websites (all browsers on every agent) and timed exceptions to them.</summary>
public sealed partial class WebFilterService(IKoshiDbContext db, AuditWriter audit, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly IdnMapping Idn = new();

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$")]
    private static partial Regex HostRegex();

    public async Task<IReadOnlyList<BlockedSiteDto>> ListAsync(CancellationToken ct = default)
    {
        var now = Now;
        var sites = await db.BlockedSites.AsNoTracking()
            .Include(s => s.Allowances.Where(a => a.RevokedAt == null && a.ExpiresAt > now)).ThenInclude(a => a.Device)
            .OrderBy(s => s.Domain).ToListAsync(ct);
        return sites.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<BlockedSiteDto>> AddAsync(CreateBlockedSitesRequest r, CancellationToken ct = default)
    {
        var domains = r.Domains.Select(NormalizeDomain).ToList();
        var invalid = r.Domains.Where((_, i) => domains[i] is null).ToList();
        if (invalid.Count > 0) throw Errors.Validation(nameof(r.Domains), $"Noto‘g‘ri sayt manzili: {string.Join(", ", invalid.Take(5))}");

        var wanted = domains.Select(d => d!).Distinct(StringComparer.Ordinal).ToList();
        var existing = await db.BlockedSites.Where(s => wanted.Contains(s.Domain)).Select(s => s.Domain).ToListAsync(ct);
        var now = Now;
        var added = new List<string>();
        foreach (var domain in wanted.Except(existing, StringComparer.Ordinal))
        {
            db.BlockedSites.Add(new BlockedSite
            {
                Id = Guid.NewGuid(), Domain = domain, Category = Clean(r.Category), Note = Clean(r.Note),
                IsEnabled = true, CreatedAt = now, UpdatedAt = now, CreatedByUserId = currentUser.UserId,
            });
            added.Add(domain);
        }
        if (added.Count > 0)
        {
            audit.Record("webfilter.block", "BlockedSite", null, new { domains = added, r.Category });
            await db.SaveChangesAsync(ct);
        }
        return await ListAsync(ct);
    }

    public async Task<BlockedSiteDto> UpdateAsync(Guid id, UpdateBlockedSiteRequest r, CancellationToken ct = default)
    {
        var site = await db.BlockedSites.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw Errors.NotFound("BlockedSite");
        site.Category = Clean(r.Category);
        site.Note = Clean(r.Note);
        site.IsEnabled = r.IsEnabled;
        site.UpdatedAt = Now;
        audit.Record("webfilter.update", "BlockedSite", id, new { site.Domain, site.IsEnabled });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(s => s.Id == id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var site = await db.BlockedSites.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw Errors.NotFound("BlockedSite");
        db.BlockedSites.Remove(site);
        audit.Record("webfilter.unblock", "BlockedSite", id, new { site.Domain });
        await db.SaveChangesAsync(ct);
    }

    public async Task<BlockedSiteDto> GrantAsync(Guid siteId, GrantSiteAllowanceRequest r, CancellationToken ct = default)
    {
        var site = await db.BlockedSites.FirstOrDefaultAsync(s => s.Id == siteId, ct) ?? throw Errors.NotFound("BlockedSite");
        if (r.DeviceId is { } deviceId && !await db.Devices.AnyAsync(d => d.Id == deviceId && d.DeletedAt == null, ct))
            throw Errors.Validation(nameof(r.DeviceId), "Qurilma topilmadi.");
        var now = Now;
        db.SiteAllowances.Add(new SiteAllowance
        {
            Id = Guid.NewGuid(), BlockedSiteId = site.Id, DeviceId = r.DeviceId, ExpiresAt = now.AddMinutes(r.Minutes),
            Reason = Clean(r.Reason), CreatedByUserId = currentUser.UserId, CreatedByEmail = currentUser.Email, CreatedAt = now,
        });
        audit.Record("webfilter.allow", "BlockedSite", site.Id, new { site.Domain, r.Minutes, r.DeviceId, r.Reason });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(s => s.Id == siteId);
    }

    public async Task RevokeAllowanceAsync(Guid allowanceId, CancellationToken ct = default)
    {
        var a = await db.SiteAllowances.Include(x => x.BlockedSite).FirstOrDefaultAsync(x => x.Id == allowanceId, ct) ?? throw Errors.NotFound("SiteAllowance");
        if (a.RevokedAt is not null) return;
        a.RevokedAt = Now;
        audit.Record("webfilter.allow_revoke", "BlockedSite", a.BlockedSiteId, new { a.BlockedSite?.Domain, a.DeviceId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Enabled blocked sites for a device with the end of the longest active allowance that applies to it.</summary>
    public async Task<IReadOnlyList<AgentBlockedSiteDto>> ForDeviceAsync(Guid deviceId, CancellationToken ct = default)
    {
        var now = Now;
        var rows = await db.BlockedSites.AsNoTracking().Where(s => s.IsEnabled)
            .Select(s => new
            {
                s.Domain,
                AllowedUntil = s.Allowances
                    .Where(a => a.RevokedAt == null && a.ExpiresAt > now && (a.DeviceId == null || a.DeviceId == deviceId))
                    .Max(a => (DateTime?)a.ExpiresAt),
            })
            .ToListAsync(ct);
        return rows.Select(r => new AgentBlockedSiteDto(r.Domain, r.AllowedUntil)).ToList();
    }

    /// <summary>"https://www.YouTube.com/watch?v=1" → "youtube.com". Returns null for anything that is not a host name.</summary>
    public static string? NormalizeDomain(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().ToLowerInvariant();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.Trim('.');
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[2..];
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        try
        {
            s = Idn.GetAscii(s);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return HostRegex().IsMatch(s) ? s : null;
    }

    private static BlockedSiteDto ToDto(BlockedSite s) => new(
        s.Id, s.Domain, s.Category, s.Note, s.IsEnabled, s.CreatedAt,
        s.Allowances.OrderBy(a => a.ExpiresAt)
            .Select(a => new SiteAllowanceDto(a.Id, a.DeviceId, a.Device?.DisplayName, a.ExpiresAt, a.Reason, a.CreatedByEmail, a.CreatedAt)).ToList());

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
