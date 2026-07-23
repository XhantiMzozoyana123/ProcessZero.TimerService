using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessZero.TimerService.Dtos;
using ProcessZero.TimerService.Jobs;
using System.Net.Http;
using Timer = System.Threading.Timer;

var builder = WebApplication.CreateBuilder(args);

var timerApiKey = builder.Configuration["TimerApiKey"] 
    ?? throw new InvalidOperationException("TimerApiKey is required.");

var mainApiUrl = builder.Configuration["MainApi:BaseUrl"] 
    ?? throw new InvalidOperationException("MainApi:BaseUrl is required.");

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddLogging();

// Configure base addresses for service communication
builder.Services.AddHttpClient("MainApi");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p =>
        p.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseRouting();

app.MapGet("/health", () => Results.Ok(new
{
    service = "ProcessZero Timer Service",
    status = "running",
    time = DateTime.UtcNow
}));

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/timer"))
    {
        if (!context.Request.Headers.TryGetValue("X-Timer-Api-Key", out var apiKey) || apiKey != timerApiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or missing API key");
            return;
        }
    }
    await next();
});

var api = app.MapGroup("/api/timer");

// Start session
api.MapPost("/sessions/start", (StartSessionRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    var now = DateTime.UtcNow;
    SessionManager.StartSession(req.UserId, req.DeviceInfo, now);
    return Results.Ok(new { sessionId = SessionManager.GetSessionId(req.UserId), startedAt = now });
});

// Heartbeat
api.MapPost("/sessions/{sessionId:int}/heartbeat", async (string sessionId, HttpRequest req) =>
{
    var form = await req.ReadFromJsonAsync<HeartbeatRequest>();
    if (form == null || string.IsNullOrWhiteSpace(form.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    var session = SessionManager.GetSession(form.UserId);
    if (session == null || session.Id.ToString() != sessionId)
        return Results.NotFound(new { error = "Session not found" });

    session.LastHeartbeat = DateTime.UtcNow;
    return Results.Ok(new { success = true, heartbeatAt = session.LastHeartbeat });
});

// End session
api.MapPost("/sessions/{sessionId:int}/end", async (string sessionId, HttpRequest req) =>
{
    var form = await req.ReadFromJsonAsync<HeartbeatRequest>();
    if (form == null || string.IsNullOrWhiteSpace(form.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    var session = SessionManager.GetSession(form.UserId);
    if (session == null || session.Id.ToString() != sessionId)
        return Results.NotFound(new { error = "Session not found" });

    var elapsed = DateTime.UtcNow - session.StartedAt;
    SessionManager.EndSession(form.UserId);
    return Results.Ok(new { success = true, elapsedMinutes = elapsed.TotalMinutes });
});

// Active session
api.MapGet("/sessions/active", async ([AsParameters] UserQuery q, HttpClient http) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    var session = SessionManager.GetSession(q.UserId);
    if (session == null)
        return Results.Ok(new { session = (object?)null });

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{mainApiUrl}/api/credit/remaining-hours");
        request.Headers.Add("X-Timer-Api-Key", timerApiKey);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var remainingHours = await response.Content.ReadFromJsonAsync<RemainingHoursResponse>(cancellationToken: CancellationToken.None);
        return Results.Ok(new { session = new { session.Id, session.StartedAt, remainingHours = remainingHours?.RemainingHours ?? 0 } });
    }
    catch
    {
        return Results.Ok(new { session = new { session.Id, session.StartedAt, remainingHours = 0 } });
    }
});

// Remaining hours
api.MapGet("/remaining-hours", async ([AsParameters] UserQuery q, HttpClient http) =>
{
    if (string.IsNullOrWhiteSpace(q.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{mainApiUrl}/api/credit/remaining-hours");
        request.Headers.Add("X-Timer-Api-Key", timerApiKey);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RemainingHoursResponse>(cancellationToken: CancellationToken.None);
        if (result != null)
            return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to get remaining hours from main API");
    }

    return Results.Problem("Unable to retrieve remaining hours");
});

// Consume credits
api.MapPost("/wallet/consume", async (WalletOperationRequest req, HttpClient http) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{mainApiUrl}/api/credit/consume");
        request.Headers.Add("X-Timer-Api-Key", timerApiKey);
        request.Content = JsonContent.Create(req);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WalletOperationResponse>(cancellationToken: CancellationToken.None);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to consume credits via main API");
        return Results.Problem("Failed to consume credits");
    }
});

// Check balance
api.MapPost("/wallet/check-balance", async (CheckBalanceRequest req, HttpClient http) =>
{
    if (string.IsNullOrWhiteSpace(req.UserId))
        return Results.BadRequest(new { error = "UserId is required" });

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{mainApiUrl}/api/credit/check");
        request.Headers.Add("X-Timer-Api-Key", timerApiKey);
        request.Content = JsonContent.Create(req.RequiredCredits);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckBalanceResponse>(cancellationToken: CancellationToken.None);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to check balance via main API");
        return Results.Problem("Failed to check balance");
    }
});

// Background timer: every minute, process active sessions for credit consumption
var timer = new Timer(async _ =>
{
    try
    {
        await new ConsumptionBackgroundJob(app.Logger, app.Services.GetRequiredService<IConfiguration>())
            .ProcessActiveSessionsAsync(mainApiUrl);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error in background consumption job");
    }
}, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

app.Run();

// In-memory session store
public static class SessionManager
{
    private static readonly Dictionary<string, UserSession> _sessions = new();
    private static int _nextId = 1;

    public static void StartSession(string userId, string? deviceInfo, DateTime startedAt)
    {
        EndSession(userId);
        _sessions[userId] = new UserSession
        {
            Id = _nextId++,
            UserId = userId,
            StartedAt = startedAt,
            LastHeartbeat = startedAt,
            DeviceInfo = deviceInfo
        };
    }

    public static UserSession? GetSession(string userId)
    {
        return _sessions.TryGetValue(userId, out var session) ? session : null;
    }

    public static string GetSessionId(string userId)
    {
        return GetSession(userId)?.Id.ToString() ?? string.Empty;
    }

    public static void EndSession(string userId)
    {
        _sessions.Remove(userId);
    }

    public static IEnumerable<UserSession> GetActiveSessions()
    {
        return _sessions.Values.ToList();
    }
}

public class UserSession
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string? DeviceInfo { get; set; }
}
