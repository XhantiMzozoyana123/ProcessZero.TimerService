using Microsoft.EntityFrameworkCore;
using ProcessZero.TimerService.Entities;

namespace ProcessZero.TimerService.Data;

public class TimerDbContext : DbContext
{
    public TimerDbContext(DbContextOptions<TimerDbContext> options) : base(options) { }

    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<ConsumptionConfig> ConsumptionConfigs => Set<ConsumptionConfig>();
    public DbSet<UserWallet> UserWallets => Set<UserWallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.DeviceInfo).HasMaxLength(500);
            entity.Property(e => e.MinutesConsumed).HasColumnType("decimal(18,4)");
            entity.Property(e => e.CreditsConsumed).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<ConsumptionConfig>(entity =>
        {
            entity.ToTable("ConsumptionConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreditsPerHour).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<UserWallet>(entity =>
        {
            entity.ToTable("UserWallets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.CreditBalance).HasColumnType("decimal(18,4)");
            entity.Property(e => e.TotalCreditsPurchased).HasColumnType("decimal(18,4)");
            entity.Property(e => e.TotalCreditsConsumed).HasColumnType("decimal(18,4)");
            entity.Property(e => e.SubscriptionStatus).HasMaxLength(50);
            entity.HasIndex(e => e.UserId).IsUnique();
        });
    }
}