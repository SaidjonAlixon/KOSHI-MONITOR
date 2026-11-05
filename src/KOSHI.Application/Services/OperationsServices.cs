using System.Text.RegularExpressions;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class AuditQueryService(IKoshiDbContext db)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery q, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action.StartsWith(q.Action.Trim()));
        if (q.ActorUserId is { } u) query = query.Where(a => a.ActorUserId == u);
        if (q.From is { } f) { var from = TelemetryService.ToUtc(f); query = query.Where(a => a.OccurredAt >= from); }
        if (q.To is { } t) { var to = TelemetryService.ToUtc(t); query = query.Where(a => a.OccurredAt < to); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(a => a.Action.Contains(s) || (a.ActorEmail != null && a.ActorEmail.Contains(s)) || (a.EntityId != null && a.EntityId.Contains(s)));
        }
        query = q.SortDir == "asc" ? query.OrderBy(a => a.OccurredAt) : query.OrderByDescending(a => a.OccurredAt);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items.Select(ToDto).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<AuditLogDto> GetAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Errors.NotFound("Audit entry"));

    private static AuditLogDto ToDto(AuditLog a) => new(a.Id, a.OccurredAt, a.ActorType.ToString(), a.ActorUserId, a.ActorEmail, a.ActorDeviceId,
        a.Action, a.EntityType, a.EntityId, a.Success, a.IpAddress, a.Details);
}

public sealed class AlertService(IKoshiDbContext db, AuditWriter audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<AlertDto>> ListAsync(AlertQuery q, CancellationToken ct = default)
    {
        var query = db.Alerts.AsNoTracking().Include(a => a.Device).AsQueryable();
        if (q.Open is true) query = query.Where(a => a.ResolvedAt == null && a.AcknowledgedAt == null);
        if (q.Open is false) query = query.Where(a => a.ResolvedAt != null || a.AcknowledgedAt != null);
        if (q.Type is not null && Enum.TryParse<AlertType>(q.Type, out var type)) query = query.Where(a => a.Type == type);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(a => a.Message.Contains(q.Search.Trim()));
        query = q.SortDir == "asc" ? query.OrderBy(a => a.CreatedAt) : query.OrderByDescending(a => a.CreatedAt);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<AlertDto>(items.Select(ToDto).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<AlertDto> AcknowledgeAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await db.Alerts.Include(a => a.Device).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw Errors.NotFound("Alert");
        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = clock.GetUtcNow().UtcDateTime;
            alert.AcknowledgedByUserId = currentUser.UserId;
            audit.Record("alert.acknowledge", "Alert", id, new { type = alert.Type.ToString() });
            await db.SaveChangesAsync(ct);
        }
        return ToDto(alert);
    }

    private static AlertDto ToDto(Alert a) => new(a.Id, a.DeviceId, a.Device?.DisplayName, a.Type.ToString(), a.Severity.ToString(), LocalizeMessage(a.Message), a.CreatedAt, a.AcknowledgedAt, a.ResolvedAt);

    private static readonly Regex Restricted = new(@"^Restricted application '([^']+)' was detected on (.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Heartbeat = new(@"^(.+) has not sent a heartbeat since (.+?)(?: UTC)?\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string LocalizeMessage(string message)
    {
        var r = Restricted.Match(message);
        if (r.Success) return $"«{r.Groups[1].Value}» dasturi {r.Groups[2].Value} kompyuterida taqiqqa qaramay ochildi.";
        var h = Heartbeat.Match(message);
        if (h.Success) return $"{h.Groups[1].Value} kompyuteri {h.Groups[2].Value} dan beri aloqa yubormadi — oflayn bo‘lishi mumkin.";
        return message;
    }
}

public sealed class OrganizationService(IKoshiDbContext db, AuditWriter audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<EmployeeDto>> EmployeesAsync(EmployeeQuery q, CancellationToken ct = default)
    {
        var departmentId = DataScope.EffectiveDepartment(currentUser, q.DepartmentId);
        var query = db.Employees.AsNoTracking().Include(e => e.Department).Where(e => e.DeletedAt == null);
        if (departmentId is { } d) query = query.Where(e => e.DepartmentId == d);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToUpper();
            query = query.Where(e => e.FullName.ToUpper().Contains(s) || e.EmployeeNumber.ToUpper().Contains(s) || (e.Position != null && e.Position.ToUpper().Contains(s)));
        }
        query = (q.SortBy?.ToLowerInvariant(), q.SortDir) switch
        {
            ("employeenumber", "desc") => query.OrderByDescending(e => e.EmployeeNumber),
            ("employeenumber", _) => query.OrderBy(e => e.EmployeeNumber),
            ("position", "desc") => query.OrderByDescending(e => e.Position),
            ("position", _) => query.OrderBy(e => e.Position),
            (_, "desc") => query.OrderByDescending(e => e.FullName),
            _ => query.OrderBy(e => e.FullName),
        };
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(e => new EmployeeDto(e.Id, e.FullName, e.EmployeeNumber, e.Email, e.Position, e.DepartmentId, e.Department != null ? e.Department.Name : null,
                e.WorkScheduleId, e.IsActive, e.IsDemo, e.Devices.Count(d => d.DeletedAt == null)))
            .ToListAsync(ct);
        return new PagedResult<EmployeeDto>(items, q.Page, q.PageSize, total);
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(UpsertEmployeeRequest r, CancellationToken ct = default)
    {
        await ValidateEmployeeAsync(r, null, ct);
        var now = Now;
        var e = new Employee { Id = Guid.NewGuid(), CreatedAt = now };
        ApplyEmployee(e, r, now);
        db.Employees.Add(e);
        audit.Record("employee.create", "Employee", e.Id, new { e.EmployeeNumber, e.DepartmentId });
        await db.SaveChangesAsync(ct);
        return await EmployeeAsync(e.Id, ct);
    }

    public async Task<EmployeeDto> UpdateEmployeeAsync(Guid id, UpsertEmployeeRequest r, CancellationToken ct = default)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw Errors.NotFound("Employee");
        DataScope.EffectiveDepartment(currentUser, e.DepartmentId);
        await ValidateEmployeeAsync(r, id, ct);
        ApplyEmployee(e, r, Now);
        audit.Record("employee.update", "Employee", id, new { e.EmployeeNumber, e.DepartmentId, e.IsActive });
        await db.SaveChangesAsync(ct);
        return await EmployeeAsync(id, ct);
    }

    /// <summary>Soft-deletes (archives) the employee; devices stay but are unassigned. History is kept for reports.</summary>
    /// <summary>Permanently removes the employee. Their computers stay and become unassigned; the tabel number is freed.</summary>
    public async Task DeleteEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Employees.Include(x => x.Devices).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Xodim");
        DataScope.EffectiveDepartment(currentUser, e.DepartmentId);
        var now = Now;
        var unassigned = e.Devices.Select(d => d.Id).ToList();
        foreach (var d in e.Devices) { d.EmployeeId = null; d.UpdatedAt = now; }
        db.Employees.Remove(e);
        audit.Record("employee.delete", "Employee", id, new { e.FullName, e.EmployeeNumber, unassignedDevices = unassigned });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DepartmentDto>> DepartmentsAsync(CancellationToken ct = default)
    {
        var departmentId = DataScope.EffectiveDepartment(currentUser, null);
        var q = db.Departments.AsNoTracking();
        if (departmentId is { } d) q = q.Where(x => x.Id == d);
        return await q.OrderBy(x => x.Name)
            .Select(x => new DepartmentDto(x.Id, x.Name, x.Branch, x.Employees.Count(e => e.DeletedAt == null), x.IsDemo)).ToListAsync(ct);
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(UpsertDepartmentRequest r, CancellationToken ct = default)
    {
        var name = r.Name.Trim();
        if (await db.Departments.AnyAsync(d => d.Name == name, ct)) throw Errors.Conflict("Department already exists.");
        var d = new Department { Id = Guid.NewGuid(), Name = name, Branch = r.Branch?.Trim(), CreatedAt = Now };
        db.Departments.Add(d);
        audit.Record("department.create", "Department", d.Id, new { d.Name, d.Branch });
        await db.SaveChangesAsync(ct);
        return new DepartmentDto(d.Id, d.Name, d.Branch, 0, false);
    }

    public async Task DeleteDepartmentAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Bo‘lim");
        var active = await db.Employees.CountAsync(e => e.DepartmentId == id && e.DeletedAt == null, ct);
        if (active > 0) throw Errors.Conflict($"Bu bo‘limda {active} ta xodim bor. Avval ularni boshqa bo‘limga o‘tkazing yoki o‘chiring, so‘ng bo‘limni o‘chiring.");
        // Detach any archived employees so the foreign key stays valid.
        var archived = await db.Employees.Where(e => e.DepartmentId == id).ToListAsync(ct);
        foreach (var e in archived) e.DepartmentId = null;
        db.Departments.Remove(d);
        audit.Record("department.delete", "Department", id, new { d.Name, d.Branch });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WorkScheduleDto>> SchedulesAsync(CancellationToken ct = default) =>
        (await db.WorkSchedules.AsNoTracking().OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(ct))
        .Select(Policies.WorkScheduleMapping.ToDto).ToList();

    public async Task<WorkScheduleDto> UpsertScheduleAsync(Guid? id, UpsertWorkScheduleRequest r, CancellationToken ct = default)
    {
        if (Domain.Scheduling.ScheduleCalculator.ResolveTimeZone(r.TimeZoneId) == TimeZoneInfo.Utc && r.TimeZoneId is not ("UTC" or "Etc/UTC"))
            throw Errors.Validation("TimeZoneId", "Unknown time zone.");
        if (r.WorkStart == r.WorkEnd) throw Errors.Validation("WorkEnd", "Work end must differ from work start.");
        if ((r.BreakStart is null) != (r.BreakEnd is null)) throw Errors.Validation("BreakEnd", "Break start and end must both be set or both be empty.");

        var now = Now;
        WorkSchedule s;
        if (id is { } existingId)
            s = await db.WorkSchedules.FirstOrDefaultAsync(x => x.Id == existingId, ct) ?? throw Errors.NotFound("Work schedule");
        else
        {
            s = new WorkSchedule { Id = Guid.NewGuid(), CreatedAt = now };
            db.WorkSchedules.Add(s);
        }
        s.Name = r.Name.Trim();
        s.TimeZoneId = r.TimeZoneId;
        s.WorkDaysMask = r.WorkDays.Distinct().Aggregate(0, (m, d) => m | (1 << (int)d));
        s.WorkStart = r.WorkStart;
        s.WorkEnd = r.WorkEnd;
        s.BreakStart = r.BreakStart;
        s.BreakEnd = r.BreakEnd;
        s.UpdatedAt = now;
        audit.Record(id is null ? "schedule.create" : "schedule.update", "WorkSchedule", s.Id, new { s.Name, s.TimeZoneId, s.WorkDaysMask });
        await db.SaveChangesAsync(ct);
        return Policies.WorkScheduleMapping.ToDto(s);
    }

    private async Task<EmployeeDto> EmployeeAsync(Guid id, CancellationToken ct) =>
        await db.Employees.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new EmployeeDto(e.Id, e.FullName, e.EmployeeNumber, e.Email, e.Position, e.DepartmentId, e.Department != null ? e.Department.Name : null,
                e.WorkScheduleId, e.IsActive, e.IsDemo, e.Devices.Count(d => d.DeletedAt == null)))
            .FirstAsync(ct);

    private async Task ValidateEmployeeAsync(UpsertEmployeeRequest r, Guid? id, CancellationToken ct)
    {
        DataScope.EffectiveDepartment(currentUser, r.DepartmentId ?? currentUser.DepartmentScopeId);
        var number = r.EmployeeNumber.Trim();
        if (await db.Employees.AnyAsync(e => e.EmployeeNumber == number && e.Id != id, ct)) throw Errors.Conflict("Employee number already exists.");
        if (r.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct)) throw Errors.Validation("DepartmentId", "Department not found.");
        if (r.WorkScheduleId is { } w && !await db.WorkSchedules.AnyAsync(x => x.Id == w, ct)) throw Errors.Validation("WorkScheduleId", "Work schedule not found.");
    }

    private static void ApplyEmployee(Employee e, UpsertEmployeeRequest r, DateTime now)
    {
        e.FullName = r.FullName.Trim();
        e.EmployeeNumber = r.EmployeeNumber.Trim();
        e.Email = r.Email?.Trim().ToLowerInvariant();
        e.Position = r.Position?.Trim();
        e.DepartmentId = r.DepartmentId;
        e.WorkScheduleId = r.WorkScheduleId;
        e.IsActive = r.IsActive;
        e.UpdatedAt = now;
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
