namespace ProcessZero.TimerService.Entities;

public class ConsumptionConfig
{
    public int Id { get; set; }
    public decimal CreditsPerHour { get; set; } = 0.2m;
    public int CheckIntervalMinutes { get; set; } = 1;
    public int MaxSessionMinutes { get; set; } = 480;
    public bool IsEnabled { get; set; } = true;
    public int GracePeriodMinutes { get; set; } = 0;
    public int InitialFreeHours { get; set; } = 5;
    public bool EnforceAccessBlock { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}