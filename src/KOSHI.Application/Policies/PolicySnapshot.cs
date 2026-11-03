using KOSHI.Contracts;
using KOSHI.Domain;

namespace KOSHI.Application.Policies;

/// <summary>Immutable published content of a policy version. Agents and reports only ever see snapshots.</summary>
public sealed record PolicySnapshot
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public PolicyModeDto Mode { get; init; }
    public bool EnforcementApproved { get; init; }
    public int Priority { get; init; } = 100;
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public int IdleThresholdSeconds { get; init; } = 300;
    public int SamplingIntervalSeconds { get; init; } = 3;
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public WindowTitleCaptureDto WindowTitleCapture { get; init; } = WindowTitleCaptureDto.Masked;
    public bool TrackBackgroundProcesses { get; init; } = true;
    public bool ScreenshotsEnabled { get; init; }
    public Guid? WorkScheduleId { get; init; }
    public WorkScheduleDto? WorkSchedule { get; init; }
    public List<AgentApplicationRuleDto> Rules { get; init; } = [];
    public List<PolicyAssignmentDto> Assignments { get; init; } = [];
}

public sealed record EffectivePolicy(Guid PolicyId, int Version, DateTime? PublishedAt, PolicySnapshot Snapshot);

public static class WorkScheduleMapping
{
    public static WorkScheduleDto ToDto(WorkSchedule s) => new(
        s.Id, s.Name, s.TimeZoneId,
        Enum.GetValues<DayOfWeek>().Where(d => WorkDays.Contains(s.WorkDaysMask, d)).ToList(),
        s.WorkStart, s.WorkEnd, s.BreakStart, s.BreakEnd, s.IsDefault);

    public static WorkSchedule ToEntity(WorkScheduleDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        TimeZoneId = d.TimeZoneId,
        WorkDaysMask = d.WorkDays.Aggregate(0, (m, day) => m | (1 << (int)day)),
        WorkStart = d.WorkStart,
        WorkEnd = d.WorkEnd,
        BreakStart = d.BreakStart,
        BreakEnd = d.BreakEnd,
        IsDefault = d.IsDefault,
    };
}
