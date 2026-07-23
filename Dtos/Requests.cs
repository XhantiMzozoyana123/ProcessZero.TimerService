namespace ProcessZero.TimerService.Dtos;

public record StartSessionRequest(string UserId, string? DeviceInfo);
public record HeartbeatRequest(string UserId);
public record UserQuery(string UserId);

public class RemainingHoursResponse
{
    public decimal RemainingHours { get; set; }
}

public class CheckBalanceResponse
{
    public decimal CreditBalance { get; set; }
    public bool HasSufficientCredits { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class WalletOperationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal NewBalance { get; set; }
    public decimal CreditsConsumed { get; set; }
}
