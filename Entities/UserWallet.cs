namespace ProcessZero.TimerService.Entities;

public class UserWallet
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public decimal CreditBalance { get; set; }
    public decimal TotalCreditsPurchased { get; set; }
    public decimal TotalCreditsConsumed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public int? SubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }
}