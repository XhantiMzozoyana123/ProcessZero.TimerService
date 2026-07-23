using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProcessZero.TimerService.Data;
using ProcessZero.TimerService.Dtos;
using ProcessZero.TimerService.Entities;

namespace ProcessZero.TimerService.Services;

public class ConsumptionService : IConsumptionService
{
    private readonly IDbContextFactory<TimerDbContext> _contextFactory;
    private readonly IUserWalletService _walletService;
    private readonly ILogger<ConsumptionService> _logger;

    public ConsumptionService(
        IDbContextFactory<TimerDbContext> contextFactory,
        IUserWalletService walletService,
        ILogger<ConsumptionService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Session Management ──

    public async Task<UserSessionDto> StartSessionAsync(string userId, string? deviceInfo = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.UserSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

        if (existing != null)
        {
            existing.IsActive = false;
            existing.SessionEndUtc = DateTime.UtcNow;
            existing.LastHeartbeatUtc = DateTime.UtcNow;
        }

        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            UserId = userId,
            SessionStartUtc = now,
            LastHeartbeatUtc = now,
            LastConsumptionProcessedUtc = now,
            IsActive = true,
            DeviceInfo = deviceInfo,
            MinutesConsumed = 0,
            CreditsConsumed = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.UserSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Started usage session {SessionId} for user {UserId}", session.Id, userId);

        return MapToSessionDto(session, 0, 0);
    }

    public async Task<SessionHeartbeatResponseDto> EndSessionAsync(int sessionId, string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.IsActive, cancellationToken);

        if (session == null)
        {
            return new SessionHeartbeatResponseDto
            {
                Success = false,
                Message = "No active session found."
            };
        }

        var now = DateTime.UtcNow;
        var elapsedMinutes = (decimal)(now - session.SessionStartUtc).TotalMinutes;
        var graceMinutes = config.GracePeriodMinutes;

        decimal totalCreditsOwed = 0;
        if (config.IsEnabled && elapsedMinutes > graceMinutes)
        {
            var chargeableMinutes = elapsedMinutes - graceMinutes;
            totalCreditsOwed = chargeableMinutes * config.CreditsPerHour / 60m;
        }

        decimal creditsToConsume = Math.Max(0, totalCreditsOwed - session.CreditsConsumed);

        if (creditsToConsume > 0)
        {
            try
            {
                var consumeResult = await _walletService.ConsumeCreditsAsync(userId,
                    new ConsumeCreditsRequest
                    {
                        CreditAmount = creditsToConsume,
                        Description = $"App usage: {elapsedMinutes - graceMinutes:F1} minutes",
                        RelatedEntityType = "ActiveUsage",
                        RelatedEntityId = sessionId
                    }, cancellationToken);

                if (!consumeResult.Success)
                {
                    _logger.LogWarning("Failed to consume credits for session {SessionId}: {Message}",
                        sessionId, consumeResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming credits for session {SessionId}", sessionId);
            }
        }

        session.IsActive = false;
        session.SessionEndUtc = now;
        session.LastHeartbeatUtc = now;
        session.MinutesConsumed = elapsedMinutes;
        session.CreditsConsumed = totalCreditsOwed;
        session.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ended usage session {SessionId} for user {UserId}. Consumed {Credits} credits for {Minutes} minutes",
            sessionId, userId, creditsToConsume, elapsedMinutes);

        decimal? remainingBalance = null;
        try
        {
            var balance = await _walletService.CheckCreditBalanceAsync(userId, 0, cancellationToken);
            remainingBalance = balance.CreditBalance;
        }
        catch { }

        return new SessionHeartbeatResponseDto
        {
            Success = true,
            IsConsuming = false,
            CreditsConsumed = creditsToConsume,
            MinutesElapsed = (double)elapsedMinutes,
            RemainingCreditBalance = remainingBalance,
            Message = "Session ended."
        };
    }

    public async Task<SessionHeartbeatResponseDto> HeartbeatAsync(int sessionId, string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.IsActive, cancellationToken);

        if (session == null)
        {
            var blockedSession = await context.UserSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.IsBlocked, cancellationToken);

            if (blockedSession != null)
            {
                return new SessionHeartbeatResponseDto
                {
                    Success = false,
                    IsConsuming = false,
                    IsBlocked = true,
                    Message = "Session blocked due to insufficient credits. Please top up to continue."
                };
            }

            return new SessionHeartbeatResponseDto
            {
                Success = false,
                Message = "No active session found. Start a new session."
            };
        }

        var now = DateTime.UtcNow;
        var elapsedMinutes = (decimal)(now - session.SessionStartUtc).TotalMinutes;

        if (config.MaxSessionMinutes > 0 && elapsedMinutes > config.MaxSessionMinutes)
        {
            session.IsActive = false;
            session.SessionEndUtc = now;
            session.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);

            return new SessionHeartbeatResponseDto
            {
                Success = false,
                IsConsuming = false,
                Message = $"Session exceeded maximum duration of {config.MaxSessionMinutes} minutes."
            };
        }

        session.LastHeartbeatUtc = now;
        session.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        var graceMinutes = config.GracePeriodMinutes;
        var isConsuming = config.IsEnabled && elapsedMinutes > graceMinutes;
        var chargeableMinutes = isConsuming ? elapsedMinutes - graceMinutes : 0;
        var estimatedCredits = chargeableMinutes * config.CreditsPerHour / 60m;

        bool isBlocked = false;
        string blockMessage = string.Empty;

        if (config.EnforceAccessBlock && config.IsEnabled)
        {
            if (elapsedMinutes > config.InitialFreeHours * 60)
            {
                isBlocked = true;
                blockMessage = $"Initial {config.InitialFreeHours} free hours exhausted. Please top up credits to continue.";
            }
            else if (isConsuming)
            {
                try
                {
                    var balance = await _walletService.CheckCreditBalanceAsync(userId, estimatedCredits, cancellationToken);
                    if (!balance.HasSufficientCredits)
                    {
                        isBlocked = true;
                        blockMessage = $"Insufficient credits. Required: {estimatedCredits:F4}, Available: {balance.CreditBalance:F4}. Please top up.";
                    }
                }
                catch { }
            }
        }

        return new SessionHeartbeatResponseDto
        {
            Success = true,
            IsConsuming = isConsuming,
            IsBlocked = isBlocked,
            CreditsConsumed = Math.Round(estimatedCredits, 4),
            MinutesElapsed = (double)elapsedMinutes,
            Message = isBlocked ? blockMessage : (isConsuming ? $"Consuming at {config.CreditsPerHour} credits/hour" : "Within grace period")
        };
    }

    public async Task<UserSessionDto?> GetActiveSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        var session = await context.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

        if (session == null) return null;

        var elapsedMinutes = (decimal)(DateTime.UtcNow - session.SessionStartUtc).TotalMinutes;
        var chargeableMinutes = Math.Max(0, elapsedMinutes - config.GracePeriodMinutes);
        var estimatedCredits = chargeableMinutes * config.CreditsPerHour / 60m;

        return MapToSessionDto(session, (double)elapsedMinutes, estimatedCredits);
    }

    public async Task<List<UserSessionDto>> GetSessionHistoryAsync(string userId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var sessions = await context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SessionStartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => MapToSessionDto(s, 0, 0)).ToList();
    }

    // ── Admin Management ──

    public async Task<ConsumptionConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        return new ConsumptionConfigDto
        {
            Id = config.Id,
            CreditsPerHour = config.CreditsPerHour,
            CheckIntervalMinutes = config.CheckIntervalMinutes,
            MaxSessionMinutes = config.MaxSessionMinutes,
            IsEnabled = config.IsEnabled,
            GracePeriodMinutes = config.GracePeriodMinutes,
            InitialFreeHours = config.InitialFreeHours,
            EnforceAccessBlock = config.EnforceAccessBlock,
            UpdatedAt = config.UpdatedAt
        };
    }

    public async Task<ConsumptionConfigDto> UpdateConfigAsync(UpdateConsumptionConfigDto dto, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        config.CreditsPerHour = dto.CreditsPerHour;
        config.CheckIntervalMinutes = dto.CheckIntervalMinutes;
        config.MaxSessionMinutes = dto.MaxSessionMinutes;
        config.IsEnabled = dto.IsEnabled;
        config.GracePeriodMinutes = dto.GracePeriodMinutes;
        config.InitialFreeHours = dto.InitialFreeHours;
        config.EnforceAccessBlock = dto.EnforceAccessBlock;
        config.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated consumption config: {Rate} credits/hr, enabled={Enabled}, maxSession={Max}min, grace={Grace}min",
            config.CreditsPerHour, config.IsEnabled, config.MaxSessionMinutes, config.GracePeriodMinutes);

        return new ConsumptionConfigDto
        {
            Id = config.Id,
            CreditsPerHour = config.CreditsPerHour,
            CheckIntervalMinutes = config.CheckIntervalMinutes,
            MaxSessionMinutes = config.MaxSessionMinutes,
            IsEnabled = config.IsEnabled,
            GracePeriodMinutes = config.GracePeriodMinutes,
            InitialFreeHours = config.InitialFreeHours,
            EnforceAccessBlock = config.EnforceAccessBlock,
            UpdatedAt = config.UpdatedAt
        };
    }

    public async Task<List<UserSessionDto>> GetAllActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        var sessions = await context.UserSessions
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.SessionStartUtc)
            .ToListAsync(cancellationToken);

        return sessions.Select(s =>
        {
            var elapsed = (double)(DateTime.UtcNow - s.SessionStartUtc).TotalMinutes;
            var chargeable = Math.Max(0, (decimal)elapsed - config.GracePeriodMinutes);
            var estimatedCredits = chargeable * config.CreditsPerHour / 60m;
            return MapToSessionDto(s, elapsed, estimatedCredits);
        }).ToList();
    }

    public async Task<bool> ForceEndSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.IsActive, cancellationToken);

        if (session == null) return false;

        var now = DateTime.UtcNow;
        session.IsActive = false;
        session.SessionEndUtc = now;
        session.LastHeartbeatUtc = now;
        session.MinutesConsumed = (decimal)(now - session.SessionStartUtc).TotalMinutes;
        session.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Admin force-ended session {SessionId} for user {UserId}", sessionId, session.UserId);

        return true;
    }

    public async Task<ConsumptionStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var activeSessions = await context.UserSessions.CountAsync(s => s.IsActive, cancellationToken);
        var todaySessions = await context.UserSessions.CountAsync(s => s.SessionStartUtc >= todayStart, cancellationToken);
        var monthSessions = await context.UserSessions.CountAsync(s => s.SessionStartUtc >= monthStart, cancellationToken);
        var todayCredits = await context.UserSessions
            .Where(s => s.SessionStartUtc >= todayStart)
            .SumAsync(s => (decimal?)s.CreditsConsumed, cancellationToken) ?? 0;
        var monthCredits = await context.UserSessions
            .Where(s => s.SessionStartUtc >= monthStart)
            .SumAsync(s => (decimal?)s.CreditsConsumed, cancellationToken) ?? 0;
        var todayMinutes = await context.UserSessions
            .Where(s => s.SessionStartUtc >= todayStart)
            .SumAsync(s => (decimal?)s.MinutesConsumed, cancellationToken) ?? 0;
        var monthMinutes = await context.UserSessions
            .Where(s => s.SessionStartUtc >= monthStart)
            .SumAsync(s => (decimal?)s.MinutesConsumed, cancellationToken) ?? 0;

        return new ConsumptionStatsDto
        {
            ActiveSessionsCount = activeSessions,
            TotalSessionsToday = todaySessions,
            TotalSessionsThisMonth = monthSessions,
            TotalCreditsConsumedToday = todayCredits,
            TotalCreditsConsumedThisMonth = monthCredits,
            TotalMinutesLoggedToday = todayMinutes,
            TotalMinutesLoggedThisMonth = monthMinutes,
            Rate = config.CreditsPerHour,
            IsEnabled = config.IsEnabled
        };
    }

    // ── Backend Timer / Consumption Engine ──

    /// <summary>
    /// Processes all active sessions, consuming credits based on actual elapsed time.
    /// This is called periodically by the Hangfire background job so that credit
    /// consumption continues even when the browser is closed or the user logs out.
    /// </summary>
    public async Task<int> ProcessActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var config = await GetOrCreateConfigAsync(context, cancellationToken);

        if (!config.IsEnabled)
        {
            _logger.LogInformation("Consumption is disabled. Skipping active session processing.");
            return 0;
        }

        var now = DateTime.UtcNow;

        var activeSessions = await context.UserSessions
            .Where(s => s.IsActive && !s.IsBlocked)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var session in activeSessions)
        {
            try
            {
                var elapsedMinutes = (decimal)(now - session.SessionStartUtc).TotalMinutes;
                if (config.MaxSessionMinutes > 0 && elapsedMinutes > config.MaxSessionMinutes)
                {
                    session.IsActive = false;
                    session.SessionEndUtc = now;
                    session.LastHeartbeatUtc = now;
                    session.UpdatedAt = now;
                    _logger.LogInformation("Session {SessionId} auto-ended: exceeded max duration of {Max} minutes.",
                        session.Id, config.MaxSessionMinutes);
                    continue;
                }

                var lastProcessed = session.LastConsumptionProcessedUtc ?? session.SessionStartUtc;
                var minutesSinceLastProcess = (decimal)(now - lastProcessed).TotalMinutes;

                if (minutesSinceLastProcess <= 0) continue;

                var graceMinutes = config.GracePeriodMinutes;
                var totalChargeableMinutes = Math.Max(0, elapsedMinutes - graceMinutes);
                var alreadyConsumedMinutes = Math.Max(0, session.MinutesConsumed);

                var incrementalChargeableMinutes = totalChargeableMinutes - alreadyConsumedMinutes;

                if (incrementalChargeableMinutes > 0)
                {
                    var creditsToConsume = incrementalChargeableMinutes * config.CreditsPerHour / 60m;

                    var balance = await _walletService.CheckCreditBalanceAsync(
                        session.UserId, creditsToConsume, cancellationToken);

                    if (balance.HasSufficientCredits)
                    {
                        var consumeResult = await _walletService.ConsumeCreditsAsync(session.UserId,
                            new ConsumeCreditsRequest
                            {
                                CreditAmount = creditsToConsume,
                                Description = $"Active usage: {incrementalChargeableMinutes:F1} minutes",
                                RelatedEntityType = "ActiveUsage",
                                RelatedEntityId = session.Id
                            }, cancellationToken);

                        if (consumeResult.Success)
                        {
                            session.MinutesConsumed = totalChargeableMinutes;
                            session.CreditsConsumed += creditsToConsume;
                            session.LastConsumptionProcessedUtc = now;
                            session.LastHeartbeatUtc = now;
                            session.UpdatedAt = now;
                            processedCount++;

                            _logger.LogDebug("Consumed {Credits} credits for session {SessionId} ({Minutes} min). Balance: {Balance}",
                                creditsToConsume, session.Id, incrementalChargeableMinutes, consumeResult.NewBalance);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to consume credits for session {SessionId}: {Message}",
                                session.Id, consumeResult.Message);
                        }
                    }
                    else
                    {
                        session.IsBlocked = true;
                        session.IsActive = false;
                        session.SessionEndUtc = now;
                        session.LastHeartbeatUtc = now;
                        session.UpdatedAt = now;
                        _logger.LogInformation("Session {SessionId} blocked: insufficient credits. Required: {Required}, Available: {Available}",
                            session.Id, creditsToConsume, balance.CreditBalance);
                    }
                }
                else
                {
                    session.LastConsumptionProcessedUtc = now;
                    session.LastHeartbeatUtc = now;
                    session.UpdatedAt = now;
                    processedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing session {SessionId} in consumption job", session.Id);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Processed {Count} active sessions for credit consumption at {Time}",
            processedCount, now);

        return processedCount;
    }

    public async Task<RemainingHoursResponse> GetRemainingHoursAsync(string userId, CancellationToken cancellationToken = default)
    {
        var remainingHours = await _walletService.GetRemainingHoursAsync(userId, cancellationToken);
        return new RemainingHoursResponse { RemainingHours = remainingHours };
    }

    // ── Helpers ──

    private static async Task<ConsumptionConfig> GetOrCreateConfigAsync(TimerDbContext context, CancellationToken cancellationToken)
    {
        var config = await context.ConsumptionConfigs.FirstOrDefaultAsync(cancellationToken);

        if (config == null)
        {
            config = new ConsumptionConfig
            {
                CreditsPerHour = 0.2m,
                CheckIntervalMinutes = 1,
                MaxSessionMinutes = 480,
                IsEnabled = true,
                GracePeriodMinutes = 0,
                InitialFreeHours = 5,
                EnforceAccessBlock = true,
                UpdatedAt = DateTime.UtcNow
            };
            context.ConsumptionConfigs.Add(config);
            await context.SaveChangesAsync(cancellationToken);
        }

        return config;
    }

    private static UserSessionDto MapToSessionDto(UserSession session, double elapsedMinutes, decimal estimatedCredits)
    {
        var remainingMinutes = 0.0;
        if (session.IsActive && elapsedMinutes > 0)
        {
            remainingMinutes = 300 - elapsedMinutes;
            if (remainingMinutes < 0) remainingMinutes = 0;
        }

        return new UserSessionDto
        {
            Id = session.Id,
            UserId = session.UserId,
            SessionStartUtc = session.SessionStartUtc,
            SessionEndUtc = session.SessionEndUtc,
            MinutesConsumed = (double)session.MinutesConsumed,
            CreditsConsumed = session.CreditsConsumed,
            IsActive = session.IsActive,
            LastHeartbeatUtc = session.LastHeartbeatUtc,
            DeviceInfo = session.DeviceInfo,
            ElapsedMinutes = elapsedMinutes,
            EstimatedCreditsConsumed = estimatedCredits,
            TimeRemainingDisplay = remainingMinutes > 0
                ? $"~{Math.Ceiling(remainingMinutes / 60)}h {Math.Ceiling(remainingMinutes % 60)}m remaining"
                : null
        };
    }
}