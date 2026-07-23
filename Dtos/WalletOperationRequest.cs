namespace ProcessZero.TimerService.Dtos;

public class WalletOperationRequest
{
    public string UserId { get; set; } = string.Empty;
    public decimal CreditAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
}

public class CheckBalanceRequest
{
    public string UserId { get; set; } = string.Empty;
    public decimal RequiredCredits { get; set; }
}

