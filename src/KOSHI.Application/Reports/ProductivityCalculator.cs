using KOSHI.Contracts;
using KOSHI.Domain.Scheduling;

namespace KOSHI.Application.Reports;

public sealed record ProductivityWeights(double ApprovedUsage, double ScheduleAdherence, double ReportedActivity, double MinimumCoverage);

public sealed record ProductivityResult(
    double? Score,
    double Coverage,
    string Confidence,
    bool IsReliable,
    IReadOnlyList<ProductivityMetricDto> Metrics);

/// <summary>
/// Configurable, explainable productivity model. Each metric is a ratio in [0,1] with an explicit
/// denominator; metrics with insufficient data are reported as missing and excluded (weights are
/// re-normalised) instead of being treated as zero. Idle time and missing telemetry never reduce the
/// score by themselves; low coverage only lowers the confidence.
/// </summary>
public static class ProductivityCalculator
{
    /// <summary>Minimum denominator (seconds) before a metric is considered meaningful.</summary>
    public const double MinimumSampleSeconds = 600;

    public const string Description =
        "Score = 100 × Σ(wᵢ·mᵢ) / Σwᵢ over available metrics. " +
        "ApprovedUsage = approved foreground time / (approved + not-approved foreground time); neutral/unclassified apps are excluded. " +
        "ScheduleAdherence = active time inside the work schedule / total active time. " +
        "ReportedActivity = active time / (active + idle time) inside the work schedule. " +
        "Coverage = (active + idle + paused time inside schedule) / scheduled time; it sets the confidence level and never lowers the score. " +
        "A metric whose denominator is below 10 minutes is reported as missing.";

    public const string Disclaimer =
        "Ko'rsatkichlar faqat qaror qabul qilishga yordamchi ma'lumot. Klaviatura/sichqoncha faolligi mehnat sifatini anglatmaydi; " +
        "idle vaqt uchrashuv, qo'ng'iroq yoki hujjat o'qish bo'lishi mumkin. Xodimlarni faqat o'xshash lavozim va vazifalar doirasida taqqoslang. " +
        "Tizim avtomatik jarima yoki ishdan bo'shatish qarorlarini chiqarmaydi.";

    public static ProductivityResult Compute(TimeBreakdown t, double approvedSeconds, double notApprovedSeconds, ProductivityWeights w)
    {
        var coverage = t.ScheduledSeconds > 0 ? Math.Clamp(t.CoveredScheduledSeconds / t.ScheduledSeconds, 0, 1) : 0;

        var classified = approvedSeconds + notApprovedSeconds;
        double? approved = classified >= MinimumSampleSeconds ? approvedSeconds / classified : null;

        double? adherence = t.ActiveTotalSeconds >= MinimumSampleSeconds ? Math.Clamp(t.ActiveInScheduleSeconds / t.ActiveTotalSeconds, 0, 1) : null;

        var presence = t.ActiveInScheduleSeconds + t.IdleInScheduleSeconds;
        double? reported = presence >= MinimumSampleSeconds ? t.ActiveInScheduleSeconds / presence : null;

        var metrics = new List<ProductivityMetricDto>
        {
            new("approvedUsage", "Approved application usage", Round(approved), w.ApprovedUsage,
                "approvedForeground / (approvedForeground + notApprovedForeground)",
                approved is null ? "Less than 10 minutes of classified application usage." : null),
            new("scheduleAdherence", "Work schedule adherence", Round(adherence), w.ScheduleAdherence,
                "activeInSchedule / activeTotal",
                adherence is null ? "Less than 10 minutes of active time in the period." : null),
            new("reportedActivity", "Reported work activity", Round(reported), w.ReportedActivity,
                "activeInSchedule / (activeInSchedule + idleInSchedule)",
                reported is null ? "Less than 10 minutes of reported presence inside the schedule." : null),
            new("coverage", "Data coverage (confidence)", Round(coverage), 0,
                "(active + idle + paused inside schedule) / scheduled",
                t.ScheduledSeconds <= 0 ? "No scheduled work time in the period." : null),
        };

        double weighted = 0, weightSum = 0;
        void Use(double? value, double weight)
        {
            if (value is null || weight <= 0) return;
            weighted += value.Value * weight;
            weightSum += weight;
        }
        Use(approved, w.ApprovedUsage);
        Use(adherence, w.ScheduleAdherence);
        Use(reported, w.ReportedActivity);

        double? score = weightSum > 0 && (coverage > 0 || t.ScheduledSeconds <= 0) ? Math.Round(100 * weighted / weightSum, 1) : null;
        var confidence = coverage >= 0.8 ? "High" : coverage >= w.MinimumCoverage ? "Medium" : coverage > 0 ? "Low" : "None";
        var reliable = score is not null && coverage >= w.MinimumCoverage;
        return new ProductivityResult(score, Math.Round(coverage, 4), confidence, reliable, metrics);
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 4);
}
