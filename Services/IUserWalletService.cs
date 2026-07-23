using ProcessZero.TimerService.Dtos;

namespace ProcessZero.TimerService.Services;

public interface IUserWalletService
{
    Task<decimal> GetRemainingHoursAsync(string userId, CancellationToken cancellationToken = default);
    Task<ConsumeCreditsResponse> ConsumeCreditsAsync(string userId, ConsumeCreditsRequest request, CancellationToken cancellationToken = default);
    Task<CheckBalanceResponse> CheckCreditBalanceAsync(string userId, decimal requiredCredits, CancellationToken cancellationToken = default);
}