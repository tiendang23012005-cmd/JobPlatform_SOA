using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Messaging;

namespace Payment.Api.Services;

public class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(options.Value.Host, options.Value.Port, timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Outbox sẽ tiếp tục phát lại sau khi broker hoạt động.
            return HealthCheckResult.Degraded("Không kết nối được cổng RabbitMQ.");
        }
    }
}
