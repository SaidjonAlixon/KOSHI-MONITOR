using System.Text;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = Permissions.ReportsRead)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("daily")]
    public async Task<ActionResult<ReportDto>> Daily([FromQuery] ReportQuery q, CancellationToken ct) => Ok(await reports.DailyAsync(q, ct));

    [HttpGet("weekly")]
    public async Task<ActionResult<ReportDto>> Weekly([FromQuery] ReportQuery q, CancellationToken ct) => Ok(await reports.WeeklyAsync(q, ct));

    [HttpGet("monthly")]
    public async Task<ActionResult<ReportDto>> Monthly([FromQuery] ReportQuery q, CancellationToken ct) => Ok(await reports.MonthlyAsync(q, ct));

    [HttpGet("productivity")]
    public async Task<ActionResult<ReportDto>> Productivity([FromQuery] ReportQuery q, CancellationToken ct) => Ok(await reports.ProductivityAsync(q, ct));

    /// <summary>CSV export. type = daily | weekly | monthly | productivity. Every export is audited.</summary>
    [HttpGet("export")]
    [Authorize(Policy = Permissions.ReportsExport)]
    public async Task<IActionResult> Export([FromQuery] string type, [FromQuery] ReportQuery q, CancellationToken ct)
    {
        var (name, content) = await reports.ExportCsvAsync(type, q, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray(), "text/csv; charset=utf-8", name);
    }
}
