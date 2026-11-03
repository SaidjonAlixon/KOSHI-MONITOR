using System.Text.Json;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Policies;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class PolicyService(IKoshiDbContext db, AuditWriter audit, ICurrentUser currentUser, SettingsService settings, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Admin CRUD ----------

    public async Task<IReadOnlyList<PolicyDto>> ListAsync(CancellationToken ct = default)
    {
        var policies = await QueryPolicies().OrderBy(p => p.Priority).ThenBy(p => p.Name).ToListAsync(ct);
        var summaries = await ResultSummariesAsync(policies, ct);
        return policies.Select(p => ToDto(p, summaries.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<PolicyDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await QueryPolicies().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Policy");
        var summaries = await ResultSummariesAsync([p], ct);
        return ToDto(p, summaries.GetValueOrDefault(p.Id));
    }

    public async Task<PolicyDto> CreateAsync(UpsertPolicyRequest r, CancellationToken ct = default)
    {
        await ValidateReferencesAsync(r, ct);
        var now = Now;
        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            Version = 0,
            Status = PolicyStatus.Draft,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
        };
        Apply(policy, r, now);
        db.Policies.Add(policy);
        audit.Record("policy.create", "Policy", policy.Id, new { policy.Name, mode = policy.Mode.ToString() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(policy.Id, ct);
    }

    public async Task<PolicyDto> UpdateAsync(Guid id, UpsertPolicyRequest r, CancellationToken ct = default)
    {
        var policy = await db.Policies.Include(p => p.Assignments).FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct)
                     ?? throw Errors.NotFound("Policy");
        await ValidateReferencesAsync(r, ct);
        var before = new { policy.Name, mode = policy.Mode.ToString(), policy.IdleThresholdSeconds, policy.SamplingIntervalSeconds, capture = policy.WindowTitleCapture.ToString() };
        db.PolicyAssignments.RemoveRange(policy.Assignments);
        policy.Assignments.Clear();
        Apply(policy, r, Now);
        audit.Record("policy.update", "Policy", id, new { before, after = new { r.Name, mode = r.Mode.ToString(), r.IdleThresholdSeconds, r.SamplingIntervalSeconds, capture = r.WindowTitleCapture.ToString() } });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PolicyDto> PublishAsync(Guid id, PublishPolicyRequest r, CancellationToken ct = default)
    {
        var policy = await db.Policies.Include(p => p.Assignments).Include(p => p.Rules).Include(p => p.WorkSchedule)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct) ?? throw Errors.NotFound("Policy");

        var approve = policy.Mode == PolicyMode.Enforce && r.ApproveEnforcement;
        var snapshot = BuildSnapshot(policy, enforcementApproved: approve);

        var errors = PolicyValidator.Validate(snapshot);
        if (errors.Count > 0)
        {
            audit.Record("policy.publish", "Policy", id, new { errors = errors.Keys }, success: false);
            await db.SaveChangesAsync(ct);
            throw new AppException(400, ErrorCodes.PolicyInvalid, "Policy validation failed.", errors);
        }

        var now = Now;
        var version = policy.Version + 1;
        await AddVersionAsync(policy, version, snapshot, r.Note, now, ct);
        if (approve)
        {
            policy.EnforcementApproved = true;
            policy.EnforcementApprovedByUserId = currentUser.UserId;
        }
        audit.Record("policy.publish", "Policy", id, new { version, mode = snapshot.Mode.ToString(), snapshot.EnforcementApproved, rules = snapshot.Rules.Count, r.Note });
        await db.SaveChangesAsync(ct);
        await CreatePendingResultsAsync(policy.Id, version, snapshot.Mode, ct);
        return await GetAsync(id, ct);
    }

    public async Task<PolicyDto> RollbackAsync(Guid id, RollbackPolicyRequest r, CancellationToken ct = default)
    {
        var policy = await db.Policies.Include(p => p.Assignments).Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct) ?? throw Errors.NotFound("Policy");
        if (policy.PublishedVersion is not { } current) throw Errors.Conflict("Policy has never been published.");

        var target = r.TargetVersion ?? await db.PolicyVersions.Where(v => v.PolicyId == id && v.Version < current)
            .OrderByDescending(v => v.Version).Select(v => (int?)v.Version).FirstOrDefaultAsync(ct)
            ?? throw Errors.Conflict("There is no earlier version to roll back to.");
        if (target == current) throw Errors.Conflict("Target version is already the published version.");

        var targetVersion = await db.PolicyVersions.FirstOrDefaultAsync(v => v.PolicyId == id && v.Version == target, ct)
                            ?? throw Errors.NotFound("Policy version");
        var snapshot = Deserialize(targetVersion.Snapshot);

        // Restore the editable draft so that the next edit starts from the rolled-back content.
        db.PolicyAssignments.RemoveRange(policy.Assignments);
        db.ApplicationRules.RemoveRange(policy.Rules);
        policy.Assignments.Clear();
        policy.Rules.Clear();
        var now = Now;
        RestoreDraft(policy, snapshot, now);

        var version = policy.Version + 1;
        await AddVersionAsync(policy, version, snapshot, r.Note ?? $"Rollback to v{target}", now, ct);
        audit.Record("policy.rollback", "Policy", id, new { from = current, to = target, newVersion = version, r.Note });
        await db.SaveChangesAsync(ct);
        await CreatePendingResultsAsync(policy.Id, version, snapshot.Mode, ct);
        return await GetAsync(id, ct);
    }

    // ---------- Application rules ----------

    public async Task<IReadOnlyList<ApplicationRuleDto>> ListRulesAsync(Guid? policyId, CancellationToken ct = default)
    {
        var q = db.ApplicationRules.AsNoTracking().Where(r => r.Policy!.DeletedAt == null);
        if (policyId is { } pid) q = q.Where(r => r.PolicyId == pid);
        var rules = await q.OrderBy(r => r.ProcessName).ToListAsync(ct);
        return rules.Select(ToDto).ToList();
    }

    public async Task<ApplicationRuleDto> CreateRuleAsync(UpsertApplicationRuleRequest r, CancellationToken ct = default)
    {
        var policy = await db.Policies.FirstOrDefaultAsync(p => p.Id == r.PolicyId && p.DeletedAt == null, ct)
                     ?? throw Errors.Validation("PolicyId", "Policy not found.");
        ValidateRule(r);
        var name = PolicyValidator.NormalizeProcessName(r.ProcessName);
        if (await db.ApplicationRules.AnyAsync(x => x.PolicyId == r.PolicyId && x.ProcessName == name, ct))
            throw Errors.Conflict("A rule for this process already exists in the policy.");

        var now = Now;
        var rule = new ApplicationRule { Id = Guid.NewGuid(), PolicyId = r.PolicyId, CreatedAt = now };
        ApplyRule(rule, r, now);
        db.ApplicationRules.Add(rule);
        MarkChanged(policy, now);
        audit.Record("application_rule.create", "ApplicationRule", rule.Id, new { rule.PolicyId, rule.ProcessName, classification = rule.Classification.ToString(), restriction = rule.Restriction.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task<ApplicationRuleDto> UpdateRuleAsync(Guid id, UpsertApplicationRuleRequest r, CancellationToken ct = default)
    {
        var rule = await db.ApplicationRules.Include(x => x.Policy).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Application rule");
        if (rule.PolicyId != r.PolicyId) throw Errors.Validation("PolicyId", "Rules cannot be moved between policies.");
        ValidateRule(r);
        var name = PolicyValidator.NormalizeProcessName(r.ProcessName);
        if (await db.ApplicationRules.AnyAsync(x => x.PolicyId == r.PolicyId && x.ProcessName == name && x.Id != id, ct))
            throw Errors.Conflict("A rule for this process already exists in the policy.");

        var now = Now;
        var before = new { rule.ProcessName, classification = rule.Classification.ToString(), restriction = rule.Restriction.ToString() };
        ApplyRule(rule, r, now);
        MarkChanged(rule.Policy!, now);
        audit.Record("application_rule.update", "ApplicationRule", id, new { before, after = new { rule.ProcessName, classification = rule.Classification.ToString(), restriction = rule.Restriction.ToString() } });
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.ApplicationRules.Include(x => x.Policy).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Application rule");
        db.ApplicationRules.Remove(rule);
        MarkChanged(rule.Policy!, Now);
        audit.Record("application_rule.delete", "ApplicationRule", id, new { rule.PolicyId, rule.ProcessName });
        await db.SaveChangesAsync(ct);
    }

    // ---------- Agent-facing ----------

    public async Task<List<EffectivePolicy>> LoadPublishedAsync(CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.Status == PolicyStatus.Published && p.PublishedVersion != null)
            .Join(db.PolicyVersions.AsNoTracking(), p => new { PolicyId = p.Id, Version = p.PublishedVersion!.Value }, v => new { v.PolicyId, v.Version },
                (p, v) => new { p.Id, v.Version, p.PublishedAt, v.Snapshot })
            .ToListAsync(ct);
        return rows.Select(r => new EffectivePolicy(r.Id, r.Version, r.PublishedAt, Deserialize(r.Snapshot))).ToList();
    }

    public async Task<EffectivePolicy?> ResolveEffectiveAsync(Device device, CancellationToken ct = default)
    {
        var departmentId = device.Employee?.DepartmentId ?? (device.EmployeeId is { } e
            ? await db.Employees.Where(x => x.Id == e).Select(x => x.DepartmentId).FirstOrDefaultAsync(ct)
            : null);
        var published = await LoadPublishedAsync(ct);
        return PolicyResolver.Resolve(new DeviceTarget(device.Id, departmentId), published, Now);
    }

    public async Task<AgentPolicyDto> GetAgentPolicyAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await db.Devices.AsNoTracking().Include(d => d.Employee).FirstOrDefaultAsync(d => d.Id == deviceId, ct) ?? throw Errors.NotFound("Device");
        if (device.Status == DeviceStatus.Revoked) throw Errors.CredentialRevoked();
        var s = await settings.GetAsync(ct);
        var effective = await ResolveEffectiveAsync(device, ct);
        var snap = effective?.Snapshot;
        var schedule = snap?.WorkSchedule ?? await DefaultScheduleDtoAsync(ct);

        // Without a published policy the agent monitors with safe defaults and no window titles.
        return new AgentPolicyDto(
            effective?.PolicyId, snap?.Name, effective?.Version,
            snap?.Mode ?? PolicyModeDto.AuditOnly,
            snap?.EnforcementApproved ?? false,
            snap?.SamplingIntervalSeconds ?? 3,
            snap?.IdleThresholdSeconds ?? 300,
            snap?.HeartbeatIntervalSeconds ?? 30,
            snap?.WindowTitleCapture ?? WindowTitleCaptureDto.None,
            snap?.TrackBackgroundProcesses ?? false,
            ScreenshotsEnabled: false,
            s.AgentAutoUpdate,
            schedule,
            snap?.Rules ?? [],
            s.MonitoringNotice,
            s.SupportContact);
    }

    public async Task ReportResultAsync(Guid deviceId, PolicyResultReportRequest r, CancellationToken ct = default)
    {
        if (!await db.PolicyVersions.AnyAsync(v => v.PolicyId == r.PolicyId && v.Version == r.PolicyVersion, ct))
            throw Errors.Validation("PolicyVersion", "Unknown policy version.");

        var now = Now;
        var result = await db.PolicyExecutionResults.FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.PolicyId == r.PolicyId && x.PolicyVersion == r.PolicyVersion, ct);
        if (result is null)
        {
            result = new PolicyExecutionResult { Id = Guid.NewGuid(), DeviceId = deviceId, PolicyId = r.PolicyId, PolicyVersion = r.PolicyVersion, CreatedAt = now };
            db.PolicyExecutionResults.Add(result);
        }
        result.Adapter = r.Adapter;
        result.Mode = (PolicyMode)(int)r.Mode;
        result.Status = (PolicyExecutionStatus)(int)r.Status;
        result.Message = r.Message?.Trim();
        result.UpdatedAt = now;

        if (result.Status == PolicyExecutionStatus.Failed)
        {
            var key = $"policy-failed:{deviceId}:{r.PolicyId}:{r.PolicyVersion}";
            if (!await db.Alerts.AnyAsync(a => a.DedupKey == key && a.ResolvedAt == null, ct))
                db.Alerts.Add(new Alert
                {
                    Id = Guid.NewGuid(), DeviceId = deviceId, Type = AlertType.PolicyFailed, Severity = AlertSeverity.Warning, CreatedAt = now, DedupKey = key,
                    Message = $"Policy v{r.PolicyVersion} failed to apply via {r.Adapter}.",
                });
        }

        audit.Record("policy.result_reported", "Policy", r.PolicyId, new { r.PolicyVersion, r.Adapter, status = r.Status.ToString() }, deviceId: deviceId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DevicePolicyStatusDto> DevicePolicyStatusAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await db.Devices.AsNoTracking().Include(d => d.Employee).FirstOrDefaultAsync(d => d.Id == deviceId && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        var effective = await ResolveEffectiveAsync(device, ct);
        var results = await db.PolicyExecutionResults.AsNoTracking().Include(r => r.Policy)
            .Where(r => r.DeviceId == deviceId).OrderByDescending(r => r.UpdatedAt).Take(50).ToListAsync(ct);
        return new DevicePolicyStatusDto(deviceId, effective?.PolicyId, effective?.Snapshot.Name, effective?.Version, device.AppliedPolicyVersion,
            results.Select(r => new PolicyExecutionResultDto(r.PolicyId, r.Policy?.Name ?? string.Empty, r.PolicyVersion, r.Adapter,
                (PolicyModeDto)(int)r.Mode, (PolicyExecutionStatusDto)(int)r.Status, r.Message, r.UpdatedAt)).ToList());
    }

    public async Task<WorkScheduleDto?> DefaultScheduleDtoAsync(CancellationToken ct = default)
    {
        var s = await db.WorkSchedules.AsNoTracking().OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        return s is null ? null : WorkScheduleMapping.ToDto(s);
    }

    public static PolicySnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<PolicySnapshot>(json, Json) ?? new PolicySnapshot();

    // ---------- helpers ----------

    private IQueryable<Policy> QueryPolicies() =>
        db.Policies.AsNoTracking()
            .Include(p => p.Assignments).Include(p => p.Rules).Include(p => p.Versions)
            .Where(p => p.DeletedAt == null);

    private Task AddVersionAsync(Policy policy, int version, PolicySnapshot snapshot, string? note, DateTime now, CancellationToken ct)
    {
        _ = ct;
        db.PolicyVersions.Add(new PolicyVersion
        {
            Id = Guid.NewGuid(), PolicyId = policy.Id, Version = version, Snapshot = JsonSerializer.Serialize(snapshot, Json),
            Note = note?.Trim(), CreatedAt = now, CreatedByUserId = currentUser.UserId,
        });
        policy.Version = version;
        policy.PublishedVersion = version;
        policy.Status = PolicyStatus.Published;
        policy.PublishedAt = now;
        policy.HasUnpublishedChanges = false;
        policy.UpdatedAt = now;
        policy.UpdatedByUserId = currentUser.UserId;
        return Task.CompletedTask;
    }

    private async Task CreatePendingResultsAsync(Guid policyId, int version, PolicyModeDto mode, CancellationToken ct)
    {
        var published = await LoadPublishedAsync(ct);
        var devices = await db.Devices.AsNoTracking().Where(d => d.Status == DeviceStatus.Active && d.DeletedAt == null)
            .Select(d => new DeviceTarget(d.Id, d.Employee != null ? d.Employee.DepartmentId : null)).ToListAsync(ct);
        var now = Now;
        var targeted = devices.Where(d => PolicyResolver.Resolve(d, published, now)?.PolicyId == policyId).Select(d => d.DeviceId).ToList();
        var existing = await db.PolicyExecutionResults.Where(r => r.PolicyId == policyId && r.PolicyVersion == version).Select(r => r.DeviceId).ToListAsync(ct);
        foreach (var deviceId in targeted.Except(existing))
        {
            db.PolicyExecutionResults.Add(new PolicyExecutionResult
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, PolicyId = policyId, PolicyVersion = version, Adapter = "pending",
                Mode = (PolicyMode)(int)mode, Status = PolicyExecutionStatus.Pending, CreatedAt = now, UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static PolicySnapshot BuildSnapshot(Policy p, bool enforcementApproved) => new()
    {
        Name = p.Name,
        Description = p.Description,
        Mode = (PolicyModeDto)(int)p.Mode,
        EnforcementApproved = enforcementApproved,
        Priority = p.Priority,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        IdleThresholdSeconds = p.IdleThresholdSeconds,
        SamplingIntervalSeconds = p.SamplingIntervalSeconds,
        HeartbeatIntervalSeconds = p.HeartbeatIntervalSeconds,
        WindowTitleCapture = (WindowTitleCaptureDto)(int)p.WindowTitleCapture,
        TrackBackgroundProcesses = p.TrackBackgroundProcesses,
        ScreenshotsEnabled = p.ScreenshotsEnabled,
        WorkScheduleId = p.WorkScheduleId,
        WorkSchedule = p.WorkSchedule is null ? null : WorkScheduleMapping.ToDto(p.WorkSchedule),
        Rules = p.Rules.OrderBy(r => r.ProcessName).Select(r => new AgentApplicationRuleDto(r.Id, r.Name, r.ProcessName, r.PathPattern, r.Publisher,
            (ApplicationClassificationDto)(int)r.Classification, (ApplicationRestrictionDto)(int)r.Restriction, r.DailyLimitMinutes,
            r.RequirePassword, r.PasswordSalt, r.PasswordHash)).ToList(),
        Assignments = p.Assignments.Select(a => new PolicyAssignmentDto((PolicyAssignmentScopeDto)(int)a.Scope, a.DepartmentId, a.DeviceId)).ToList(),
    };

    private void RestoreDraft(Policy p, PolicySnapshot s, DateTime now)
    {
        p.Name = s.Name;
        p.Description = s.Description;
        p.Mode = (PolicyMode)(int)s.Mode;
        p.EnforcementApproved = s.EnforcementApproved;
        p.Priority = s.Priority;
        p.EffectiveFrom = s.EffectiveFrom;
        p.EffectiveTo = s.EffectiveTo;
        p.IdleThresholdSeconds = s.IdleThresholdSeconds;
        p.SamplingIntervalSeconds = s.SamplingIntervalSeconds;
        p.HeartbeatIntervalSeconds = s.HeartbeatIntervalSeconds;
        p.WindowTitleCapture = (WindowTitleCapture)(int)s.WindowTitleCapture;
        p.TrackBackgroundProcesses = s.TrackBackgroundProcesses;
        p.WorkScheduleId = s.WorkScheduleId;
        foreach (var a in s.Assignments)
            db.PolicyAssignments.Add(new PolicyAssignment { Id = Guid.NewGuid(), PolicyId = p.Id, Scope = (PolicyAssignmentScope)(int)a.Scope, DepartmentId = a.DepartmentId, DeviceId = a.DeviceId, CreatedAt = now });
        foreach (var r in s.Rules)
            db.ApplicationRules.Add(new ApplicationRule
            {
                Id = Guid.NewGuid(), PolicyId = p.Id, Name = r.Name, ProcessName = r.ProcessName, PathPattern = r.PathPattern, Publisher = r.Publisher,
                Classification = (ApplicationClassification)(int)r.Classification, Restriction = (ApplicationRestriction)(int)r.Restriction,
                DailyLimitMinutes = r.DailyLimitMinutes, RequirePassword = r.RequirePassword, PasswordSalt = r.PasswordSalt, PasswordHash = r.PasswordHash,
                CreatedAt = now, UpdatedAt = now,
            });
    }

    private void Apply(Policy p, UpsertPolicyRequest r, DateTime now)
    {
        p.Name = r.Name.Trim();
        p.Description = r.Description?.Trim();
        var newMode = (PolicyMode)(int)r.Mode;
        if (newMode != p.Mode) p.EnforcementApproved = false;
        p.Mode = newMode;
        p.Priority = r.Priority;
        p.EffectiveFrom = r.EffectiveFrom is { } f ? TelemetryService.ToUtc(f) : (p.EffectiveFrom == default ? now : p.EffectiveFrom);
        p.EffectiveTo = r.EffectiveTo is { } t ? TelemetryService.ToUtc(t) : null;
        p.WorkScheduleId = r.WorkScheduleId;
        p.IdleThresholdSeconds = r.IdleThresholdSeconds;
        p.SamplingIntervalSeconds = r.SamplingIntervalSeconds;
        p.HeartbeatIntervalSeconds = r.HeartbeatIntervalSeconds;
        p.WindowTitleCapture = (WindowTitleCapture)(int)r.WindowTitleCapture;
        p.TrackBackgroundProcesses = r.TrackBackgroundProcesses;
        p.ScreenshotsEnabled = false;
        p.UpdatedAt = now;
        p.UpdatedByUserId = currentUser.UserId;
        p.HasUnpublishedChanges = true;
        foreach (var a in r.Assignments.Distinct())
            p.Assignments.Add(new PolicyAssignment { Id = Guid.NewGuid(), PolicyId = p.Id, Scope = (PolicyAssignmentScope)(int)a.Scope, DepartmentId = a.DepartmentId, DeviceId = a.DeviceId, CreatedAt = now });
    }

    private async Task ValidateReferencesAsync(UpsertPolicyRequest r, CancellationToken ct)
    {
        var errors = PolicyValidator.Validate(new PolicySnapshot
        {
            Name = r.Name, Mode = PolicyModeDto.AuditOnly, IdleThresholdSeconds = r.IdleThresholdSeconds,
            SamplingIntervalSeconds = r.SamplingIntervalSeconds, HeartbeatIntervalSeconds = r.HeartbeatIntervalSeconds,
            EffectiveFrom = r.EffectiveFrom ?? DateTime.MinValue, EffectiveTo = r.EffectiveTo, Assignments = r.Assignments,
        }, requireTargets: false);
        if (errors.Count > 0) throw Errors.Validation(errors);

        if (r.WorkScheduleId is { } ws && !await db.WorkSchedules.AnyAsync(x => x.Id == ws, ct))
            throw Errors.Validation("WorkScheduleId", "Work schedule not found.");
        var deptIds = r.Assignments.Where(a => a.DepartmentId is not null).Select(a => a.DepartmentId!.Value).Distinct().ToList();
        if (deptIds.Count > 0 && await db.Departments.CountAsync(d => deptIds.Contains(d.Id), ct) != deptIds.Count)
            throw Errors.Validation("Assignments", "One or more departments not found.");
        var devIds = r.Assignments.Where(a => a.DeviceId is not null).Select(a => a.DeviceId!.Value).Distinct().ToList();
        if (devIds.Count > 0 && await db.Devices.CountAsync(d => devIds.Contains(d.Id) && d.DeletedAt == null, ct) != devIds.Count)
            throw Errors.Validation("Assignments", "One or more devices not found.");
    }

    private static void ValidateRule(UpsertApplicationRuleRequest r)
    {
        var errors = PolicyValidator.ValidateRule(PolicyValidator.NormalizeProcessName(r.ProcessName), r.PathPattern, r.Publisher, r.Classification, r.Restriction).ToArray();
        if (errors.Length > 0) throw Errors.Validation(new Dictionary<string, string[]> { ["Rule"] = errors });
    }

    private static void ApplyRule(ApplicationRule rule, UpsertApplicationRuleRequest r, DateTime now)
    {
        rule.Name = r.Name.Trim();
        rule.ProcessName = PolicyValidator.NormalizeProcessName(r.ProcessName);
        rule.PathPattern = string.IsNullOrWhiteSpace(r.PathPattern) ? null : r.PathPattern.Trim();
        rule.Publisher = string.IsNullOrWhiteSpace(r.Publisher) ? null : r.Publisher.Trim();
        rule.Classification = (ApplicationClassification)(int)r.Classification;
        rule.Restriction = (ApplicationRestriction)(int)r.Restriction;
        rule.DailyLimitMinutes = r.Restriction == ApplicationRestrictionDto.Block ? null : r.DailyLimitMinutes;
        rule.RequirePassword = r.RequirePassword && r.Restriction != ApplicationRestrictionDto.Block;
        if (!rule.RequirePassword)
        {
            rule.PasswordSalt = null;
            rule.PasswordHash = null;
        }
        else if (!string.IsNullOrEmpty(r.Password))
        {
            (rule.PasswordSalt, rule.PasswordHash) = AppLockPassword.Create(r.Password);
        }
        if (rule.RequirePassword && string.IsNullOrEmpty(rule.PasswordHash))
            throw Errors.Validation("Password", "Parol bilan himoyalash uchun parol kiriting.");
        rule.Description = r.Description?.Trim();
        rule.UpdatedAt = now;
    }

    private void MarkChanged(Policy p, DateTime now)
    {
        p.HasUnpublishedChanges = true;
        p.UpdatedAt = now;
        p.UpdatedByUserId = currentUser.UserId;
    }

    private async Task<Dictionary<Guid, PolicyResultSummaryDto>> ResultSummariesAsync(IReadOnlyList<Policy> policies, CancellationToken ct)
    {
        var ids = policies.Where(p => p.PublishedVersion != null).Select(p => p.Id).ToList();
        var rows = await db.PolicyExecutionResults.AsNoTracking()
            .Where(r => ids.Contains(r.PolicyId) && r.PolicyVersion == r.Policy!.PublishedVersion)
            .GroupBy(r => new { r.PolicyId, r.Status })
            .Select(g => new { g.Key.PolicyId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.PolicyId).ToDictionary(g => g.Key, g =>
        {
            int C(PolicyExecutionStatus s) => g.Where(x => x.Status == s).Sum(x => x.Count);
            return new PolicyResultSummaryDto(C(PolicyExecutionStatus.Pending), C(PolicyExecutionStatus.Applied), C(PolicyExecutionStatus.Failed),
                C(PolicyExecutionStatus.Unsupported), C(PolicyExecutionStatus.RequiresAdministratorApproval));
        });
    }

    private static PolicyDto ToDto(Policy p, PolicyResultSummaryDto? results) => new(
        p.Id, p.Name, p.Description, p.Version, p.PublishedVersion, p.Status.ToString(), (PolicyModeDto)(int)p.Mode, p.Priority,
        p.EnforcementApproved, p.HasUnpublishedChanges, p.EffectiveFrom, p.EffectiveTo, p.WorkScheduleId, p.IdleThresholdSeconds,
        p.SamplingIntervalSeconds, p.HeartbeatIntervalSeconds, (WindowTitleCaptureDto)(int)p.WindowTitleCapture, p.TrackBackgroundProcesses,
        p.ScreenshotsEnabled,
        p.Assignments.Select(a => new PolicyAssignmentDto((PolicyAssignmentScopeDto)(int)a.Scope, a.DepartmentId, a.DeviceId)).ToList(),
        p.Rules.OrderBy(r => r.ProcessName).Select(ToDto).ToList(),
        p.Versions.OrderByDescending(v => v.Version).Select(v => new PolicyVersionDto(v.Version, v.Note, v.CreatedAt, v.CreatedByUserId)).ToList(),
        results ?? new PolicyResultSummaryDto(0, 0, 0, 0, 0),
        p.CreatedAt, p.UpdatedAt, p.PublishedAt);

    private static ApplicationRuleDto ToDto(ApplicationRule r) => new(
        r.Id, r.PolicyId, r.Name, r.ProcessName, r.PathPattern, r.Publisher,
        (ApplicationClassificationDto)(int)r.Classification, (ApplicationRestrictionDto)(int)r.Restriction, r.DailyLimitMinutes, r.RequirePassword, r.Description);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
