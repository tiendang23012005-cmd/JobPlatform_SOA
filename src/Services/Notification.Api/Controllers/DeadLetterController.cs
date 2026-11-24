
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using Shared.Infrastructure.Messaging;

namespace Notification.Api.Controllers;

[ApiController]
[Route("api/v1/admin/dlq")]
[Authorize(Roles = "Admin")]
public class DeadLetterController(RabbitMqConnection rabbit) : ControllerBase
{
    private static readonly Dictionary<string, string> Queues = new()
    {
        ["user-registered"] = "notification.user-registered",
        ["job-approved"] = "notification.job-approved",
        ["application-submitted"] = "notification.application-submitted",
        ["application-accepted"] = "notification.application-accepted",
        ["application-rejected"] = "notification.application-rejected",
        ["wallet-topup-completed"] = "notification.wallet-topup-completed"
    };

    // Xem các queue DLQ và số message đang chờ
    [HttpGet]
    public IActionResult GetQueues()
    {
        using var channel = rabbit.CreateChannel();

        var result = Queues.Select(item =>
        {
            var dlq = item.Value + ".dlq";
            var info = channel.QueueDeclarePassive(dlq);

            return new
            {
                name = item.Key,
                queue = item.Value,
                deadLetterQueue = dlq,
                messageCount = info.MessageCount
            };
        });

        return Ok(result);
    }

    // Xem tối đa 20 message lỗi trong một DLQ
    [HttpGet("{name}")]
    public IActionResult GetMessages(string name)
    {
        if (!Queues.TryGetValue(name, out var queue))
            return NotFound(new { message = "DLQ không tồn tại." });

        using var channel = rabbit.CreateChannel();

        var dlq = queue + ".dlq";
        var messages = new List<object>();

        for (var i = 0; i < 20; i++)
        {
            var message = channel.BasicGet(dlq, autoAck: false);

            if (message is null)
                break;

            messages.Add(new
            {
                messageId = message.BasicProperties.MessageId,
                routingKey = message.RoutingKey,
                body = Encoding.UTF8.GetString(message.Body.ToArray())
            });

            channel.BasicNack(message.DeliveryTag, false, true);
        }

        return Ok(new
        {
            queue = dlq,
            messages
        });
    }

    // Đưa message lỗi trở lại queue xử lý chính
    [HttpPost("{name}/reprocess")]
    public IActionResult Reprocess(string name)
    {
        if (!Queues.TryGetValue(name, out var queue))
            return NotFound(new { message = "DLQ không tồn tại." });

        using var channel = rabbit.CreateChannel();

        var dlq = queue + ".dlq";
        var message = channel.BasicGet(dlq, autoAck: false);

        if (message is null)
            return NotFound(new { message = "DLQ không còn message." });

        try
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = message.BasicProperties.ContentType;
            properties.MessageId = message.BasicProperties.MessageId;
            properties.CorrelationId = message.BasicProperties.CorrelationId;

            channel.ConfirmSelect();

            channel.BasicPublish(
                exchange: Topology.Exchange,
                routingKey: Queues[name] == "notification.user-registered"
                    ? "UserRegistered"
                    : name switch
                    {
                        "job-approved" => "JobApproved",
                        "application-submitted" => "ApplicationSubmitted",
                        "application-accepted" => "ApplicationAccepted",
                        "application-rejected" => "ApplicationRejected",
                        "wallet-topup-completed" => "WalletTopUpCompleted",
                        _ => throw new InvalidOperationException("Routing key không hợp lệ.")
                    },
                basicProperties: properties,
                body: message.Body);

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            // Chỉ xóa message khỏi DLQ sau khi publish thành công
            channel.BasicAck(message.DeliveryTag, false);

            return Ok(new { message = "Đã gửi message về hàng đợi xử lý lại." });
        }
        catch
        {
            // Nếu gửi thất bại, giữ lại message trong DLQ
            channel.BasicNack(message.DeliveryTag, false, true);
            throw;
        }
    }
}
