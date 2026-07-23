using ProcessZero.TimerService.Dtos;

namespace ProcessZero.TimerService.Services;

public interface IConsumptionService
{
    Task<UserSessionDto> StartSessionAsync(string userId, string? deviceInfo = null, CancellationToken cancellationToken = default);
    Task<SessionHeartbeatResponseDto> HeartbeatAsync(int sessionId, string userId, CancellationToken cancellationToken = default);
    Task<SessionHeartbeatResponseDto> EndSessionAsync(int sessionId, string userId, CancellationToken cancellationToken = default);
    Task<UserSessionDto?> GetActiveSessionAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UserSessionDto>> GetSessionHistoryAsync(string userId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<ConsumptionConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
    Task<ConsumptionConfigDto> UpdateConfigAsync(UpdateConsumptionConfigDto dto, CancellationToken cancellationToken = default);
    Task<List<UserSessionDto>> GetAllActiveSessionsAsync(CancellationToken cancellationToken = default);
    Task<bool> ForceEndSessionAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<ConsumptionStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<int> ProcessActiveSessionsAsync(CancellationToken cancellationToken = default);
    Task<RemainingHoursResponse> GetRemainingHoursAsync(string userId, CancellationToken cancellationToken = default);
}