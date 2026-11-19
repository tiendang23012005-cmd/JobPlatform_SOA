using Microsoft.EntityFrameworkCore;

namespace Notification.Api.Data;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string EventType { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsRead { get; set; }
    public string DeliveryStatus { get; set; } = "Pending";   // Pending | Sent | Failed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ghi nhận sự kiện đã xử lý để consumer idempotent theo EventId.</summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.DeliveryStatus).HasMaxLength(20);
        });
        b.Entity<ProcessedEvent>().HasKey(x => x.EventId);
    }
}
