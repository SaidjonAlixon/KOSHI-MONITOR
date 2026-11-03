using System.Text.Json;
using KOSHI.Application.Abstractions;
using KOSHI.Domain;

namespace KOSHI.Application.Services;

/// <summary>
/// Adds audit entries to the current unit of work so they commit atomically with the audited change.
/// Callers must never pass secrets (passwords, codes, tokens) in <paramref name="details"/>.
/// </summary>
public sealed class AuditWriter(IKoshiDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const int MaxDetailsLength = 4000;

    public AuditLog Record(string action, string? entityType = null, object? entityId = null, object? details = null,
        bool success = true, Guid? deviceId = null, AuditActorType? actorType = null, User? actor = null)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details);
        if (json is { Length: > MaxDetailsLength }) json = JsonSerializer.Serialize(new { truncated = true });

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAt = clock.GetUtcNow().UtcDateTime,
            ActorType = actorType ?? (deviceId is not null ? AuditActorType.Device
                : actor is not null || currentUser.UserId is not null ? AuditActorType.User : AuditActorType.Anonymous),
            ActorUserId = deviceId is null ? actor?.Id ?? currentUser.UserId : null,
            ActorEmail = deviceId is null ? actor?.Email ?? currentUser.Email : null,
            ActorDeviceId = deviceId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Success = success,
            IpAddress = currentUser.IpAddress,
            Details = json,
        };
        db.AuditLogs.Add(entry);
        return entry;
    }

    public async Task RecordAndSaveAsync(string action, string? entityType = null, object? entityId = null,
        object? details = null, bool success = true, Guid? deviceId = null, CancellationToken ct = default)
    {
        Record(action, entityType, entityId, details, success, deviceId);
        await db.SaveChangesAsync(ct);
    }
}
