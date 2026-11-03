using KOSHI.Contracts;

namespace KOSHI.Application.Policies;

public sealed record DeviceTarget(Guid DeviceId, Guid? DepartmentId);

/// <summary>
/// Chooses the effective published policy for a device: most specific scope wins
/// (Device &gt; Department &gt; AllDevices), then the lowest Priority number, then the latest publish.
/// </summary>
public static class PolicyResolver
{
    public static EffectivePolicy? Resolve(DeviceTarget device, IEnumerable<EffectivePolicy> published, DateTime now)
    {
        EffectivePolicy? best = null;
        var bestRank = (Specificity: -1, Priority: int.MaxValue, Published: DateTime.MinValue);

        foreach (var p in published)
        {
            var s = p.Snapshot;
            if (s.EffectiveFrom > now || (s.EffectiveTo is { } to && to <= now)) continue;

            var specificity = Specificity(device, s.Assignments);
            if (specificity < 0) continue;

            var rank = (specificity, s.Priority, p.PublishedAt ?? DateTime.MinValue);
            if (best is null || Better(rank, bestRank))
            {
                best = p;
                bestRank = rank;
            }
        }

        return best;
    }

    private static bool Better((int Specificity, int Priority, DateTime Published) a, (int Specificity, int Priority, DateTime Published) b) =>
        a.Specificity != b.Specificity ? a.Specificity > b.Specificity
        : a.Priority != b.Priority ? a.Priority < b.Priority
        : a.Published > b.Published;

    private static int Specificity(DeviceTarget device, IEnumerable<PolicyAssignmentDto> assignments)
    {
        var result = -1;
        foreach (var a in assignments)
        {
            var s = a.Scope switch
            {
                PolicyAssignmentScopeDto.Device when a.DeviceId == device.DeviceId => 3,
                PolicyAssignmentScopeDto.Department when a.DepartmentId is not null && a.DepartmentId == device.DepartmentId => 2,
                PolicyAssignmentScopeDto.AllDevices => 1,
                _ => -1,
            };
            result = Math.Max(result, s);
        }
        return result;
    }
}
