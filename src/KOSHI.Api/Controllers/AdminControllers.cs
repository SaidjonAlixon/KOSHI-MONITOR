using KOSHI.Api.Realtime;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using KOSHI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Permissions.AuditRead)]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List([FromQuery] AuditQuery q, CancellationToken ct) => Ok(await audit.ListAsync(q, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditLogDto>> Get(Guid id, CancellationToken ct) => Ok(await audit.GetAsync(id, ct));
}

[ApiController]
[Route("api")]
public sealed class UsersController(UserService users, RoleService roles) : ControllerBase
{
    [HttpGet("permissions")]
    [Authorize(Policy = Permissions.UsersManage)]
    public ActionResult<IReadOnlyList<PermissionInfoDto>> PermissionCatalog() => Ok(RoleService.Catalog());

    [HttpPost("roles")]
    [Authorize(Policy = Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> CreateRole(UpsertRoleRequest request, CancellationToken ct) => StatusCode(201, await roles.CreateAsync(request, ct));

    [HttpPut("roles/{id:guid}")]
    [Authorize(Policy = Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> UpdateRole(Guid id, UpsertRoleRequest request, CancellationToken ct) => Ok(await roles.UpdateAsync(id, request, ct));

    [HttpDelete("roles/{id:guid}")]
    [Authorize(Policy = Permissions.RolesManage)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("users")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] PageQuery q, CancellationToken ct) => Ok(await users.ListAsync(q, ct));

    [HttpPost("users")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct) => StatusCode(201, await users.CreateAsync(request, ct));

    [HttpPut("users/{id:guid}")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => Ok(await users.UpdateAsync(id, request, ct));

    [HttpDelete("users/{id:guid}")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("roles")]
    [Authorize(Policy = Permissions.UsersManage)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> Roles(CancellationToken ct) => Ok(await roles.ListAsync(ct));
}

[ApiController]
[Route("api")]
public sealed class OrganizationController(OrganizationService org) : ControllerBase
{
    [HttpGet("employees")]
    [Authorize(Policy = Permissions.EmployeesRead)]
    public async Task<ActionResult<PagedResult<EmployeeDto>>> Employees([FromQuery] EmployeeQuery q, CancellationToken ct) => Ok(await org.EmployeesAsync(q, ct));

    [HttpPost("employees")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee(UpsertEmployeeRequest r, CancellationToken ct) => StatusCode(201, await org.CreateEmployeeAsync(r, ct));

    [HttpPut("employees/{id:guid}")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<ActionResult<EmployeeDto>> UpdateEmployee(Guid id, UpsertEmployeeRequest r, CancellationToken ct) => Ok(await org.UpdateEmployeeAsync(id, r, ct));

    [HttpDelete("employees/{id:guid}")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> ArchiveEmployee(Guid id, CancellationToken ct)
    {
        await org.ArchiveEmployeeAsync(id, ct);
        return NoContent();
    }

    [HttpGet("departments")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> Departments(CancellationToken ct) => Ok(await org.DepartmentsAsync(ct));

    [HttpPost("departments")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(UpsertDepartmentRequest r, CancellationToken ct) => StatusCode(201, await org.CreateDepartmentAsync(r, ct));

    [HttpDelete("departments/{id:guid}")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> DeleteDepartment(Guid id, CancellationToken ct)
    {
        await org.DeleteDepartmentAsync(id, ct);
        return NoContent();
    }

    [HttpGet("schedules")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public async Task<ActionResult<IReadOnlyList<WorkScheduleDto>>> Schedules(CancellationToken ct) => Ok(await org.SchedulesAsync(ct));

    [HttpPost("schedules")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<WorkScheduleDto>> CreateSchedule(UpsertWorkScheduleRequest r, CancellationToken ct) => StatusCode(201, await org.UpsertScheduleAsync(null, r, ct));

    [HttpPut("schedules/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<WorkScheduleDto>> UpdateSchedule(Guid id, UpsertWorkScheduleRequest r, CancellationToken ct) => Ok(await org.UpsertScheduleAsync(id, r, ct));
}

[ApiController]
[Route("api/support")]
[Authorize(Policy = Permissions.SupportChat)]
public sealed class SupportController(SupportService support, IHubContext<AgentHub> agents, IHubContext<MonitoringHub> monitoring) : ControllerBase
{
    [HttpGet("threads")]
    public async Task<ActionResult<IReadOnlyList<SupportThreadDto>>> Threads(CancellationToken ct) => Ok(await support.ThreadsAsync(ct));

    [HttpGet("{deviceId:guid}")]
    public async Task<ActionResult<IReadOnlyList<SupportMessageDto>>> History(Guid deviceId, CancellationToken ct)
    {
        var (messages, readIds) = await support.HistoryForAdminWithReadAsync(deviceId, ct);
        if (readIds.Count > 0)
            await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("chatMessagesRead", readIds, ct);
        return Ok(messages);
    }

    [HttpPost("{deviceId:guid}")]
    public async Task<ActionResult<SupportMessageDto>> Send(Guid deviceId, SendSupportMessageRequest request, CancellationToken ct)
    {
        var dto = await support.SendFromAdminAsync(deviceId, request.Text, ct);
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("chatMessage", dto, ct);
        await monitoring.Clients.All.SendAsync("supportMessage", dto, ct);
        return Ok(dto);
    }

    [HttpPost("{deviceId:guid}/typing")]
    public async Task<IActionResult> Typing(Guid deviceId, CancellationToken ct)
    {
        var name = User.FindFirst("email")?.Value ?? User.Identity?.Name ?? "Administrator";
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("chatTyping", name, ct);
        return NoContent();
    }

    [HttpPut("messages/{messageId:guid}")]
    public async Task<ActionResult<SupportMessageDto>> EditMessage(Guid messageId, SendSupportMessageRequest request, CancellationToken ct)
    {
        var dto = await support.UpdateMessageAsync(messageId, request.Text, ct);
        await agents.Clients.Group(AgentHub.Group(dto.DeviceId)).SendAsync("chatMessageUpdated", dto, ct);
        await monitoring.Clients.All.SendAsync("supportMessageUpdated", dto, ct);
        return Ok(dto);
    }

    [HttpDelete("messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid messageId, CancellationToken ct)
    {
        var (deviceId, id) = await support.DeleteMessageAsync(messageId, ct);
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("chatMessageDeleted", id, ct);
        await monitoring.Clients.All.SendAsync("supportMessageDeleted", new { deviceId, messageId = id }, ct);
        return NoContent();
    }

    [HttpDelete("{deviceId:guid}/clear")]
    public async Task<IActionResult> ClearThread(Guid deviceId, CancellationToken ct)
    {
        await support.ClearThreadAsync(deviceId, ct);
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("chatCleared", ct);
        await monitoring.Clients.All.SendAsync("supportCleared", deviceId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(AlertService alerts) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.AlertsRead)]
    public async Task<ActionResult<PagedResult<AlertDto>>> List([FromQuery] AlertQuery q, CancellationToken ct) => Ok(await alerts.ListAsync(q, ct));

    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Policy = Permissions.AlertsManage)]
    public async Task<ActionResult<AlertDto>> Acknowledge(Guid id, CancellationToken ct) => Ok(await alerts.AcknowledgeAsync(id, ct));
}

[ApiController]
[Route("api")]
public sealed class SystemController(SettingsService settings, DashboardService dashboard, IHubContext<AgentHub> agents) : ControllerBase
{
    [HttpGet("settings")]
    [Authorize(Policy = Permissions.SettingsRead)]
    public async Task<ActionResult<SystemSettingsDto>> GetSettings(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.SettingsManage)]
    public async Task<ActionResult<SystemSettingsDto>> UpdateSettings(UpdateSystemSettingsRequest r, CancellationToken ct)
    {
        var dto = await settings.UpdateAsync(r, ct);
        // Push instantly to all agents so protection password / notices apply right away.
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    [HttpGet("dashboard/summary")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public async Task<ActionResult<DashboardSummaryDto>> Dashboard([FromQuery] MonitoringQuery q, CancellationToken ct) => Ok(await dashboard.SummaryAsync(q, ct));
}

[ApiController]
[Route("api/health")]
public sealed class HealthController(KoshiDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<HealthDto>> Get(CancellationToken ct)
    {
        string database;
        try
        {
            database = await db.Database.CanConnectAsync(ct) ? "ok" : "unreachable";
        }
        catch (Exception)
        {
            database = "unreachable";
        }
        var version = typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "unknown";
        var dto = new HealthDto(database == "ok" ? "healthy" : "degraded", version, clock.GetUtcNow().UtcDateTime, database);
        return database == "ok" ? Ok(dto) : StatusCode(503, dto);
    }
}
