using System.Globalization;
using System.Text;
using KOSHI.Contracts;

namespace KOSHI.Application.Reports;

public static class CsvExporter
{
    public static string Write(ReportDto report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# KOSHI report;period={Escape(report.Period)};from={report.From:O};to={report.To:O};timezone={Escape(report.TimeZone)};groupBy={report.GroupBy}");
        sb.AppendLine($"# formula={Escape(report.Formula.Description)}");
        sb.AppendLine($"# weights=approvedUsage:{F(report.Formula.ApprovedUsageWeight)},scheduleAdherence:{F(report.Formula.ScheduleAdherenceWeight)},reportedActivity:{F(report.Formula.ReportedActivityWeight)},minimumCoverage:{F(report.Formula.MinimumCoverage)}");
        sb.AppendLine("group,name,position,department,devices,scheduled_h,active_h,idle_h,paused_h,break_h,outside_schedule_h,unknown_h,coverage,confidence,score,reliable");
        foreach (var r in report.Rows)
        {
            sb.AppendJoin(',',
                Escape(r.GroupKey), Escape(r.GroupName), Escape(r.Position), Escape(r.DepartmentName), r.DeviceCount.ToString(CultureInfo.InvariantCulture),
                H(r.ScheduledSeconds), H(r.ActiveSeconds), H(r.IdleSeconds), H(r.PausedSeconds), H(r.ScheduledBreakSeconds),
                H(r.OutsideScheduleSeconds), H(r.UnknownSeconds), F(r.Coverage), r.Confidence,
                r.ProductivityScore is { } s ? F(s) : "", r.IsReliable ? "yes" : "no");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Quotes values and neutralises spreadsheet formula injection (=, +, -, @, tab, CR).</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value;
        if (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r', ';']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static string H(double seconds) => (seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture);

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
