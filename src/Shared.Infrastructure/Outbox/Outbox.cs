using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Events;
using Shared.Infrastructure.Messaging;

namespace Shared.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; set; }                 // = EventId
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
}

/// <summary>DbContext của service nào dùng Outbox thì implement interface này.</summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
}

public static class OutboxExtensions
{
    /// <summary>Ghi sự kiện vào bảng Outbox; được lưu cùng transaction với dữ liệu nghiệp vụ khi SaveChanges.</summary>
    public static void AddOutbox(this IOutboxDbContext db, IntegrationEvent evt) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = evt.EventId,
            EventType = evt.EventType,
            Payload = JsonSerializer.Serialize(evt, evt.GetType())
        });
}

/// <summary>Tiến trình nền đọc Outbox và phát lên RabbitMQ (at-least-once).</summary>
public class OutboxPublisherService<TContext>(
    IServiceScopeFactory scopes, RabbitMqConnection mq, ILogger<OutboxPublisherService<TContext>> log)
    : BackgroundService where TContext : DbContext, IOutboxDbContext
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var found = 0;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                var batch = await db.OutboxMessages.Where(m => m.PublishedAt == null)
                    .OrderBy(m => m.CreatedAt).Take(50).ToListAsync(ct);
                found = batch.Count;
                foreach (var m in batch)
                {
                    mq.Publish(m.EventType, Encoding.UTF8.GetBytes(m.Payload), m.Id);
                    m.PublishedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogWarning("Outbox chưa phát được: {Msg}", ex.Message); }

            if (found == 0)
            {
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
