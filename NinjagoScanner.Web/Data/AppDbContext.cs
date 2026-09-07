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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CollectionMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.CollectionId, membership.UserId });
            entity.Property(membership => membership.Role).HasConversion<string>();
        });
    }
}
