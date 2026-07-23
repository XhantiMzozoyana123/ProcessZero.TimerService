namespace ProcessZero.TimerService.Entities;

public class UserSession
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime SessionStartUtc { get; set; }
    public DateTime? SessionEndUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public DateTime? LastConsumptionProcessedUtc { get; set; }
    public bool IsActive { get; set; }
    public bool IsBlocked { get; set; }
    public string? DeviceInfo { get; set; }
    public decimal MinutesConsumed { get; set; }
    public decimal CreditsConsumed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}