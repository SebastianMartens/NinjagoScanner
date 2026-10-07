using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NinjagoScanner.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionMembership> CollectionMemberships => Set<CollectionMembership>();
    public DbSet<AchievementUnlock> AchievementUnlocks => Set<AchievementUnlock>();
    public DbSet<GamificationProfile> GamificationProfiles => Set<GamificationProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<CollectionSharingSettings> CollectionSharingSettings => Set<CollectionSharingSettings>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeItem> TradeItems => Set<TradeItem>();
    public DbSet<TradeLogEntry> TradeLogEntries => Set<TradeLogEntry>();
    public DbSet<TradeLogItem> TradeLogItems => Set<TradeLogItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CollectionMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.CollectionId, membership.UserId });
            entity.Property(membership => membership.Role).HasConversion<string>();
        });

        builder.Entity<AchievementUnlock>(entity =>
        {
            entity.HasKey(unlock => new { unlock.CollectionId, unlock.AchievementId });
        });

        builder.Entity<GamificationProfile>(entity =>
        {
            entity.HasKey(profile => profile.CollectionId);
        });

        builder.Entity<Friendship>(entity =>
        {
            entity.HasKey(friendship => friendship.Id);
            entity.Property(friendship => friendship.Status).HasConversion<string>();
            entity.HasIndex(friendship => new { friendship.UserLowId, friendship.UserHighId }).IsUnique();
            entity.HasIndex(friendship => friendship.RequesterUserId);
            entity.HasIndex(friendship => friendship.AddresseeUserId);
        });

        builder.Entity<CollectionSharingSettings>(entity =>
        {
            entity.HasKey(settings => settings.CollectionId);
            entity.Property(settings => settings.Visibility).HasConversion<string>();
        });

        builder.Entity<Trade>(entity =>
        {
            entity.HasKey(trade => trade.Id);
            entity.Property(trade => trade.Status).HasConversion<string>();
            entity.Property(trade => trade.ConcurrencyStamp).IsConcurrencyToken();
            entity.HasIndex(trade => trade.ProposerUserId);
            entity.HasIndex(trade => trade.RecipientUserId);
            entity.HasMany(trade => trade.Items).WithOne().HasForeignKey(item => item.TradeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TradeItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Side).HasConversion<string>();
            entity.HasIndex(item => item.PhotoId);
        });

        builder.Entity<TradeLogEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.TradeId).IsUnique();
            entity.HasMany(entry => entry.Items).WithOne().HasForeignKey(item => item.TradeLogEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TradeLogItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Side).HasConversion<string>();
        });
    }
}
