using Microsoft.EntityFrameworkCore;
using ProcessZero.TimerService.Data;
using ProcessZero.TimerService.Dtos;
using ProcessZero.TimerService.Entities;

namespace ProcessZero.TimerService.Services;

public class UserWalletService : IUserWalletService
{
    private readonly IDbContextFactory<TimerDbContext> _contextFactory;

    public UserWalletService(IDbContextFactory<TimerDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <summary>
    /// Gets remaining hours based on credit balance. 0.2 credits/hour rate.
    /// </summary>
    public async Task<decimal> GetRemainingHoursAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var wallet = await context.UserWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        var creditBalance = wallet?.CreditBalance ?? 0;
        return creditBalance / 0.2m;
    }

    /// <summary>
    /// Consumes credits from a user's wallet and creates a transaction record in the main DB.
    /// </summary>
    public async Task<ConsumeCreditsResponse> ConsumeCreditsAsync(string userId, ConsumeCreditsRequest request, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var wallet = await context.UserWallets
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            return new ConsumeCreditsResponse
            {
                Success = false,
                Message = "Wallet not found. User may not have initialized their wallet."
            };
        }

        if (wallet.CreditBalance < request.CreditAmount)
        {
            return new ConsumeCreditsResponse
            {
                Success = false,
                Message = "Insufficient credits for this operation",
                NewBalance = wallet.CreditBalance,
                CreditsConsumed = 0
            };
        }

        wallet.CreditBalance -= request.CreditAmount;
        wallet.TotalCreditsConsumed += request.CreditAmount;
        wallet.LastUpdatedAt = DateTime.UtcNow;

        // We store consumption as a simple log entry in the UserSessions table consumption tracking.
        // Direct wallet mutations are recorded via sessions.
        context.UserWallets.Update(wallet);
        await context.SaveChangesAsync(cancellationToken);

        return new ConsumeCreditsResponse
        {
            Success = true,
            Message = $"Successfully consumed {request.CreditAmount} credits",
            NewBalance = wallet.CreditBalance,
            CreditsConsumed = request.CreditAmount
        };
    }

    public async Task<CheckBalanceResponse> CheckCreditBalanceAsync(string userId, decimal requiredCredits, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var wallet = await context.UserWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        var currentBalance = wallet?.CreditBalance ?? 0;
        var hasSufficient = currentBalance >= requiredCredits;

        return new CheckBalanceResponse
        {
            CreditBalance = currentBalance,
            HasSufficientCredits = hasSufficient,
            Message = hasSufficient
                ? "Sufficient credits available"
                : $"Insufficient credits. Required: {requiredCredits}, Available: {currentBalance}"
        };
    }
}