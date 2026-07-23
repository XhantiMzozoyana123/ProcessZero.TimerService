using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProcessZero.TimerService.Jobs;

/// <summary>
/// Processes active usage sessions and consumes credits via HTTP calls to the main API.
/// </summary>
public class ConsumptionBackgroundJob
{
    private readonly ILogger _logger;
    private readonly string _timerApiKey;

    public ConsumptionBackgroundJob(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _timerApiKey = configuration["TimerApiKey"] 
            ?? throw new InvalidOperationException("TimerApiKey is required.");
    }

    /// <summary>
    /// Processes all active sessions, consuming credits for elapsed time.
    /// Called periodically by Hangfire (every minute).
    /// </summary>
    public async Task ProcessActiveSessionsAsync(string mainApiUrl)
    {
        try
        {
            _logger.LogInformation("Starting active session consumption processing at {Time}", DateTime.UtcNow);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("X-Timer-Api-Key", _timerApiKey);
            var sessions = SessionManager.GetActiveSessions();
            var processed = 0;

            foreach (var session in sessions)
            {
                try
                {
                    var elapsedMinutes = (DateTime.UtcNow - session.StartedAt).TotalMinutes;
                    if (elapsedMinutes <= 0) continue;

                    var creditsToConsume = decimal.Round((decimal)elapsedMinutes * 0.2m / 60.0m, 6);
                    if (creditsToConsume <= 0) continue;

                    if (creditsToConsume > 0)
                    {
                        var response = await http.PostAsJsonAsync($"{mainApiUrl}/api/credit/consume", new
                        {
                            UserId = session.UserId,
                            CreditAmount = creditsToConsume,
                            Description = "Auto consumption from active session",
                            RelatedEntityType = "Session",
                            RelatedEntityId = session.Id
                        });

                        if (response.IsSuccessStatusCode)
                        {
                            processed++;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to consume credits for user {UserId}: {StatusCode}",
                                session.UserId, response.StatusCode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing session for user {UserId}", session.UserId);
                }
            }

            _logger.LogInformation("Completed active session consumption processing at {Time}. Sessions processed: {Count}",
                DateTime.UtcNow, processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing active sessions at {Time}", DateTime.UtcNow);
            throw;
        }
    }
}
