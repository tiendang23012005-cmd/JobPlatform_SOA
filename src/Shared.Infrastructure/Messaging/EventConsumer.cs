using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Events;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Lớp cơ sở cho consumer: queue bền vững, ack thủ công, retry 3 lần (1s, 2s) rồi chuyển Dead-letter Queue.
/// Handler phải idempotent theo EventId.
/// </summary>
public abstract class EventConsumer<TEvent>(
    RabbitMqConnection mq, IServiceScopeFactory scopes, ILogger logger) : BackgroundService
    where TEvent : IntegrationEvent
{
    private const int MaxAttempts = 3;

    protected abstract string QueueName { get; }
    protected abstract string RoutingKey { get; }
    protected abstract Task HandleAsync(TEvent evt, IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        IModel? channel = null;
        while (!ct.IsCancellationRequested && channel is null)
        {
            try { channel = CreateAndDeclare(); }
            catch (Exception ex)
            {
                logger.LogWarning("Chưa kết nối được RabbitMQ ({Queue}): {Msg}", QueueName, ex.Message);
                try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; }
            }
        }
        if (channel is null) return;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<TEvent>(json)!;
                    using var scope = scopes.CreateScope();
                    await HandleAsync(evt, scope.ServiceProvider, ct);
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Xử lý {Queue} lỗi (lần {Attempt}/{Max})", QueueName, attempt, MaxAttempts);
                    if (attempt < MaxAttempts)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(attempt), ct); } catch (OperationCanceledException) { return; }
                    }
                }
            }
            channel.BasicNack(ea.DeliveryTag, false, false); // → Dead-letter Queue
        };
        channel.BasicConsume(QueueName, autoAck: false, consumer);

        try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        channel.Dispose();
    }

    private IModel CreateAndDeclare()
    {
        var ch = mq.CreateChannel();
        ch.ExchangeDeclare(Topology.Exchange, ExchangeType.Topic, durable: true);
        ch.ExchangeDeclare(Topology.DeadLetterExchange, ExchangeType.Direct, durable: true);

        var dlq = QueueName + ".dlq";
        ch.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(dlq, Topology.DeadLetterExchange, QueueName);

        ch.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = Topology.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = QueueName
        });
        ch.QueueBind(QueueName, Topology.Exchange, RoutingKey);
        ch.BasicQos(0, 10, false);
        return ch;
    }
}
