using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KOSHI.Api.Infrastructure;
using KOSHI.Api.Security;
using KOSHI.Application;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Infrastructure;
using KOSHI.Infrastructure.Persistence;
using KOSHI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "KOSHI_");

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Destructure.ByTransforming<LoginRequest>(_ => new { redacted = true })
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.AddKoshiApplication(o => builder.Configuration.GetSection("Auth").Bind(o));
builder.Services.AddKoshiInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<KOSHI.Application.Abstractions.ICurrentUser, HttpCurrentUser>();
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<KOSHI.Application.Abstractions.IMonitoringNotifier, KOSHI.Api.Realtime.SignalRMonitoringNotifier>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var problem = new ValidationProblemDetails(ctx.ModelState) { Status = 400, Title = "One or more validation errors occurred." };
        problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        problem.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KoshiExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecurityTransformer>());

// ---------- Authentication & authorization ----------
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(AuthSchemes.Admin)
    .AddJwtBearer(AuthSchemes.Admin, o =>
    {
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.CreateKey(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.HttpContext.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query["access_token"] is { Count: 1 } token)
                    ctx.Token = token.ToString();
                return Task.CompletedTask;
            },
        };
    })
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(AuthSchemes.Device, _ => { });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization(o =>
{
    var admin = new AuthorizationPolicyBuilder(AuthSchemes.Admin).RequireAuthenticatedUser().Build();
    o.DefaultPolicy = admin;
    o.FallbackPolicy = admin;
    foreach (var permission in Permissions.All)
        o.AddPolicy(permission, p => p.AddAuthenticationSchemes(AuthSchemes.Admin).RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
    o.AddPolicy(AuthSchemes.DevicePolicy, p => p.AddAuthenticationSchemes(AuthSchemes.Device).RequireAuthenticatedUser().RequireClaim(AuthSchemes.DeviceIdClaim));
});

// ---------- Rate limiting ----------
var rl = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Results.Problem(statusCode: 429, title: "Too many requests.", extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.RateLimited })
            .ExecuteAsync(ctx.HttpContext);
    };
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("GlobalPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("activation", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("ActivationPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("device", c => RateLimitPartition.GetTokenBucketLimiter(c.Request.Headers.Authorization.ToString() is { Length: > 40 } h ? h[..40] : Ip(c),
        _ => new TokenBucketRateLimiterOptions { TokenLimit = rl.GetValue("DeviceBurst", 60), TokensPerPeriod = rl.GetValue("DevicePerMinute", 120), ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
});

// ---------- CORS (explicit allowlist only) ----------
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
              ?? (builder.Configuration["Cors:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0)
        p.WithOrigins(origins).AllowCredentials().WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Authorization", "Content-Type", "X-KOSHI-Client").WithExposedHeaders("Content-Disposition");
}));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("Network:KnownProxies").Get<string[]>() ?? [])
        if (System.Net.IPAddress.TryParse(proxy, out var ip)) o.KnownProxies.Add(ip);
    if (builder.Configuration.GetValue("Network:TrustAllProxies", false))
    {
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Network:UseHsts", true)) app.UseHsts();
if (app.Configuration.GetValue("Network:HttpsRedirection", false)) app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    if (!ctx.Request.Path.StartsWithSegments("/swagger")) h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    h.CacheControl = "no-store";
    await next();
});

app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/api/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Configuration.GetValue("OpenApi:Enabled", app.Environment.IsDevelopment()))
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "KOSHI API v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapControllers();
app.MapHub<KOSHI.Api.Realtime.MonitoringHub>(KOSHI.Api.Realtime.MonitoringHub.Path);

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    await KOSHI.Application.Services.RoleService.ReloadCacheAsync(
        scope.ServiceProvider.GetRequiredService<KoshiDbContext>(), app.Services.GetRequiredService<RolePermissionCache>());
}

await app.RunAsync();

public partial class Program;
