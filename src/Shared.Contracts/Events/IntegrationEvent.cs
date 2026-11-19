namespace Shared.Contracts.Events;

/// <summary>Lớp cơ sở của mọi sự kiện gửi qua RabbitMQ (BR-18).</summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public abstract string EventType { get; }
    public int SchemaVersion { get; init; } = 1;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
