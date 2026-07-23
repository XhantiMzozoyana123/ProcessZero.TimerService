namespace ProcessZero.TimerService.Dtos;

public record StartSessionRequest(string UserId, string? DeviceInfo);
public record HeartbeatRequest(string UserId);
public record UserQuery(string UserId);

public class WalletOperationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal NewBalance { get; set; }
    public decimal CreditsConsumed { get; set; }
}
