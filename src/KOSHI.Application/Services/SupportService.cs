using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

/// <summary>Two-way support chat between an employee's agent and administrators.</summary>
public sealed class SupportService(IKoshiDbContext db, ICurrentUser currentUser, AuditWriter audit, TimeProvider clock)
{
    private const int MaxText = 2000;

    public async Task<IReadOnlyList<SupportThreadDto>> ThreadsAsync(CancellationToken ct = default)
    {
        var rows = await db.SupportMessages.AsNoTracking()
            .GroupBy(m => m.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                Unread = g.Count(x => x.Sender == SupportSender.Device && x.ReadAt == null),
                LastAt = g.Max(x => x.CreatedAt),
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.DeviceId).ToList();
        var devices = await db.Devices.AsNoTracking().Include(d => d.Employee)
            .Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var previews = await db.SupportMessages.AsNoTracking()
            .Where(m => ids.Contains(m.DeviceId))
            .GroupBy(m => m.DeviceId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToDictionaryAsync(m => m.DeviceId, m => m.Text, ct);

        return rows.OrderByDescending(r => r.LastAt).Select(r =>
        {
            var d = devices.GetValueOrDefault(r.DeviceId);
            var preview = previews.GetValueOrDefault(r.DeviceId);
            return new SupportThreadDto(r.DeviceId, d?.DisplayName ?? "—", d?.Employee?.FullName, r.Unread, r.LastAt,
                preview is { Length: > 80 } ? preview[..80] + "…" : preview);
        }).ToList();
    }

    public async Task<IReadOnlyList<SupportMessageDto>> HistoryForAdminAsync(Guid deviceId, CancellationToken ct = default)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId && d.DeletedAt == null, ct)) throw Errors.NotFound("Device");
        var msgs = await db.SupportMessages.Where(m => m.DeviceId == deviceId).OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var unread = msgs.Where(m => m.Sender == SupportSender.Device && m.ReadAt == null).ToList();
        foreach (var m in unread) m.ReadAt = now;
        if (unread.Count > 0) await db.SaveChangesAsync(ct);
        return msgs.Select(ToDto).ToList();
    }

    public async Task<SupportMessageDto> SendFromAdminAsync(Guid deviceId, string text, CancellationToken ct = default)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId && d.DeletedAt == null, ct)) throw Errors.NotFound("Device");
        var msg = new SupportMessage
        {
            Id = Guid.NewGuid(), DeviceId = deviceId, Sender = SupportSender.Admin,
            SenderUserId = currentUser.UserId, SenderName = currentUser.Email,
            Text = Trim(text), CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.SupportMessages.Add(msg);
        audit.Record("support.reply", "Device", deviceId);
        await db.SaveChangesAsync(ct);
        return ToDto(msg);
    }

    // ---------- Agent side ----------

    public async Task<SupportMessageDto> SendFromDeviceAsync(Guid deviceId, string text, CancellationToken ct = default)
    {
        var msg = new SupportMessage
        {
            Id = Guid.NewGuid(), DeviceId = deviceId, Sender = SupportSender.Device,
            Text = Trim(text), CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.SupportMessages.Add(msg);
        await db.SaveChangesAsync(ct);
        return ToDto(msg);
    }

    /// <summary>Agent poll: returns messages since the given time and marks admin messages as delivered.</summary>
    public async Task<AgentSupportPollDto> PollForDeviceAsync(Guid deviceId, DateTime? since, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var from = since ?? now.AddDays(-7);
        var msgs = await db.SupportMessages.Where(m => m.DeviceId == deviceId && m.CreatedAt > from).OrderBy(m => m.CreatedAt).Take(200).ToListAsync(ct);
        var unseen = msgs.Where(m => m.Sender == SupportSender.Admin && m.ReadAt == null).ToList();
        foreach (var m in unseen) m.ReadAt = now;
        if (unseen.Count > 0) await db.SaveChangesAsync(ct);
        return new AgentSupportPollDto(msgs.Select(ToDto).ToList(), now);
    }

    private static string Trim(string text)
    {
        var t = text.Trim();
        return t.Length > MaxText ? t[..MaxText] : t;
    }

    private static SupportMessageDto ToDto(SupportMessage m) =>
        new(m.Id, m.DeviceId, m.Sender.ToString(), m.SenderName, m.Text, m.CreatedAt, m.ReadAt);
}
