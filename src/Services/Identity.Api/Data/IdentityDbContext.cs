using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Outbox;

namespace Identity.Api.Data;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IOutboxDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<Profile>(p => p.UserId);
        });
        b.Entity<Profile>().Property(x => x.FullName).HasMaxLength(100);
        b.Entity<EmailOtp>().HasIndex(x => x.UserId);
        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(100);
            e.HasIndex(x => x.PublishedAt);
        });
    }
}
