using Microsoft.EntityFrameworkCore;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class LeaderboardDbContext(DbContextOptions<LeaderboardDbContext> options) : DbContext(options)
{
    public DbSet<InstallationParticipationEntity> Installations => Set<InstallationParticipationEntity>();
    public DbSet<InstallationProfileEntity> InstallationProfiles => Set<InstallationProfileEntity>();
    public DbSet<PlayerSampleStateEntity> PlayerSampleStates => Set<PlayerSampleStateEntity>();
    public DbSet<XpGainEventEntity> XpGainEvents => Set<XpGainEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstallationParticipationEntity>(entity =>
        {
            entity.ToTable("installation_participation");
            entity.HasKey(x => x.InstallationId);
            entity.Property(x => x.LastHeartbeatAtUtc).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<InstallationProfileEntity>(entity =>
        {
            entity.ToTable("installation_profile");
            entity.HasKey(x => new { x.InstallationId, x.PlayerId });
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.LastActiveAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne(x => x.Installation).WithMany(x => x.Profiles)
                .HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.IsActive, x.LastActiveAtUtc });
            entity.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<PlayerSampleStateEntity>(entity =>
        {
            entity.ToTable("player_sample_state");
            entity.HasKey(x => x.PlayerId);
            entity.Property(x => x.PlayerId).ValueGeneratedNever();
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            entity.Property(x => x.LastSampledAtUtc).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<XpGainEventEntity>(entity =>
        {
            entity.ToTable("xp_gain_event", table => table.HasCheckConstraint("ck_positive_gain", "\"Gain\" > 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.ObservedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.ObservedAtUtc, x.PlayerId });
        });
    }
}
