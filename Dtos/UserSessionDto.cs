namespace ProcessZero.TimerService.Dtos;

public class UserSessionDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime SessionStartUtc { get; set; }
    public DateTime? SessionEndUtc { get; set; }
    public double MinutesConsumed { get; set; }
    public decimal CreditsConsumed { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public string? DeviceInfo { get; set; }
    public double ElapsedMinutes { get; set; }
    public decimal EstimatedCreditsConsumed { get; set; }
    public string? TimeRemainingDisplay { get; set; }
}

public class SessionHeartbeatResponseDto
{
    public bool Success { get; set; }
    public bool IsConsuming { get; set; }
    public bool IsBlocked { get; set; }
    public decimal CreditsConsumed { get; set; }
    public double MinutesElapsed { get; set; }
    public decimal? RemainingCreditBalance { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ConsumptionConfigDto
{
    public int Id { get; set; }
    public decimal CreditsPerHour { get; set; }
    public int CheckIntervalMinutes { get; set; }
    public int MaxSessionMinutes { get; set; }
    public bool IsEnabled { get; set; }
    public int GracePeriodMinutes { get; set; }
    public int InitialFreeHours { get; set; }
    public bool EnforceAccessBlock { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateConsumptionConfigDto
{
    public decimal CreditsPerHour { get; set; }
    public int CheckIntervalMinutes { get; set; }
    public int MaxSessionMinutes { get; set; }
    public bool IsEnabled { get; set; }
    public int GracePeriodMinutes { get; set; }
    public int InitialFreeHours { get; set; }
    public bool EnforceAccessBlock { get; set; }
}

public class ConsumptionStatsDto
{
    public int ActiveSessionsCount { get; set; }
    public int TotalSessionsToday { get; set; }
    public int TotalSessionsThisMonth { get; set; }
    public decimal TotalCreditsConsumedToday { get; set; }
    public decimal TotalCreditsConsumedThisMonth { get; set; }
    public decimal TotalMinutesLoggedToday { get; set; }
    public decimal TotalMinutesLoggedThisMonth { get; set; }
    public decimal Rate { get; set; }
    public bool IsEnabled { get; set; }
}

public class RemainingHoursResponse
{
    public decimal RemainingHours { get; set; }
}

public class ConsumeCreditsRequest
{
    public decimal CreditAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
}

public class ConsumeCreditsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal NewBalance { get; set; }
    public decimal CreditsConsumed { get; set; }
}

public class CheckBalanceResponse
{
    public decimal CreditBalance { get; set; }
    public bool HasSufficientCredits { get; set; }
    public string Message { get; set; } = string.Empty;
}