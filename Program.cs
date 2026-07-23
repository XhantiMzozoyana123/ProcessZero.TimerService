using System.Security.Claims;
using System.Text.Encodings.Web;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProcessZero.TimerService.Data;
using ProcessZero.TimerService.Dtos;
using ProcessZero.TimerService.Jobs;
using ProcessZero.TimerService.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found or is empty.");

var timerApiKey = builder.Configuration["TimerApiKey"] 
    ?? throw new InvalidOperationException("TimerApiKey is required.");

// ── Database ──
builder.Services.AddDbContextFactory<TimerDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

// ── Hangfire ──
builder.Services.AddHangfire(config =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
          {
              TablesPrefix = "HangfireTimer"
          }));
});

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2; // Lightweight - just processes sessions
});

// ── Services ──
builder.Services.AddScoped<IUserWalletService, UserWalletService>();
builder.Services.AddScoped<IConsumptionService, ConsumptionService>();
builder.Services.AddScoped<ConsumptionBackgroundJob>();

// ── CORS (allow main API to call us) ──
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p =>
        p.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// ── Auth: API Key validation ──
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", options =>
    {
        options.ApiKey = timerApiKey;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ── Middleware ──
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ── Hangfire Dashboard (accessible only internally) ──
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardNoAuthFilter() }
});

// ── Hangfire Recurring Job ──
// This job runs every minute to process active sessions and consume credits.
// This runs in its own Docker container, independent of the main API,
// so timers continue running during API deployments.
RecurringJob.AddOrUpdate<ConsumptionBackgroundJob>(
    "process-active-session-consumption",
    job => job.ProcessActiveSessionsAsync(),
    Cron.MinuteInterval(1));

// ══════════════════════════════════════════════════════════════════
// API ENDPOINTS
// ══════════════════════════════════════════════════════════════════

var api = app.MapGroup("/api/timer").RequireAuthorization("ApiKey");

// ── Health ──
api.MapGet("/health", () => Results.Ok(new
{
    service = "ProcessZero Timer Service",
    status = "running",
    time = DateTime.UtcNow
}));

// ── Session Management ──
api.MapPost("/sessions/start", async (IConsumptionService svc, StartSessionRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var session = await svc.StartSessionAsync(req.UserId, req.DeviceInfo);
    return Results.Ok(session);
});

api.MapPost("/sessions/{sessionId:int}/heartbeat", async (IConsumptionService svc, int sessionId, SessionActionRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var result = await svc.HeartbeatAsync(sessionId, req.UserId);
    return Results.Ok(result);
});

api.MapPost("/sessions/{sessionId:int}/end", async (IConsumptionService svc, int sessionId, SessionActionRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var result = await svc.EndSessionAsync(sessionId, req.UserId);
    return Results.Ok(result);
});

api.MapGet("/sessions/active", async (IConsumptionService svc, [AsParameters] UserQuery q) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var session = await svc.GetActiveSessionAsync(q.UserId);
    if (session == null) return Results.Ok(new { session = (object?)null });
    return Results.Ok(new { session });
});

api.MapGet("/sessions/history", async (IConsumptionService svc, [AsParameters] SessionHistoryQuery q) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var sessions = await svc.GetSessionHistoryAsync(q.UserId, q.Page, q.PageSize);
    return Results.Ok(new { sessions });
});

// ── Remaining Hours ──
api.MapGet("/remaining-hours", async (IConsumptionService svc, [AsParameters] UserQuery q) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var result = await svc.GetRemainingHoursAsync(q.UserId);
    return Results.Ok(result);
});

// ── Admin: Config ──
api.MapGet("/admin/config", async (IConsumptionService svc) =>
{
    var config = await svc.GetConfigAsync();
    return Results.Ok(config);
});

api.MapPut("/admin/config", async (IConsumptionService svc, UpdateConsumptionConfigDto dto) =>
{
    var config = await svc.UpdateConfigAsync(dto);
    return Results.Ok(config);
});

// ── Admin: Sessions ──
api.MapGet("/admin/sessions", async (IConsumptionService svc) =>
{
    var sessions = await svc.GetAllActiveSessionsAsync();
    return Results.Ok(new { sessions });
});

api.MapPost("/admin/sessions/{sessionId:int}/force-end", async (IConsumptionService svc, int sessionId) =>
{
    var result = await svc.ForceEndSessionAsync(sessionId);
    return result ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Session not found" });
});

api.MapGet("/admin/stats", async (IConsumptionService svc) =>
{
    var stats = await svc.GetStatsAsync();
    return Results.Ok(stats);
});

// ── Wallet endpoints (proxy from main API) ──
api.MapGet("/wallet/remaining-hours", async (IUserWalletService svc, [AsParameters] UserQuery q) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var hours = await svc.GetRemainingHoursAsync(q.UserId);
    return Results.Ok(new RemainingHoursResponse { RemainingHours = hours });
});

api.MapPost("/wallet/consume", async (IUserWalletService svc, WalletConsumeRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var result = await svc.ConsumeCreditsAsync(req.UserId, new ConsumeCreditsRequest
    {
        CreditAmount = req.CreditAmount,
        Description = req.Description,
        RelatedEntityType = req.RelatedEntityType,
        RelatedEntityId = req.RelatedEntityId
    });
    return Results.Ok(result);
});

api.MapPost("/wallet/check-balance", async (IUserWalletService svc, WalletCheckBalanceRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required." });
    var result = await svc.CheckCreditBalanceAsync(req.UserId, req.RequiredCredits);
    return Results.Ok(result);
});

app.Run();

// ══════════════════════════════════════════════════════════════════
// REQUEST MODELS (kept in Program.cs for minimal API style)
// ══════════════════════════════════════════════════════════════════

record StartSessionRequest(string UserId, string? DeviceInfo);
record SessionActionRequest(string UserId);
record UserQuery(string UserId);
record SessionHistoryQuery(string UserId, int Page = 1, int PageSize = 20);
record WalletConsumeRequest(string UserId, decimal CreditAmount, string Description, string? RelatedEntityType = null, int? RelatedEntityId = null);
record WalletCheckBalanceRequest(string UserId, decimal RequiredCredits);

// ══════════════════════════════════════════════════════════════════
// ALLOW ALL DASHBOARD ACCESS FILTER
// ══════════════════════════════════════════════════════════════════

public class HangfireDashboardNoAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

// ══════════════════════════════════════════════════════════════════
// API KEY AUTHENTICATION SCHEME
// ══════════════════════════════════════════════════════════════════

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public const string ApiKeyHeaderName = "X-Timer-Api-Key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API Key header"));
        }

        if (!string.Equals(extractedApiKey, Options.ApiKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "TimerService") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}