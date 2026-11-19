using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Notification.Api.Services;
using Shared.Contracts.Events;
using Shared.Infrastructure.Messaging;

namespace Notification.Api.Consumers;

public class UserRegisteredConsumer(RabbitMqConnection mq, IServiceScopeFactory scopes, ILogger<UserRegisteredConsumer> logger)
    : EventConsumer<UserRegisteredEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.user-registered";
    protected override string RoutingKey => "UserRegistered";

    protected override async Task HandleAsync(UserRegisteredEvent evt, IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<NotificationDbContext>();
        var mail = sp.GetRequiredService<IEmailSender>();

        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == evt.EventId, ct)) return; // idempotent

        await mail.SendAsync(evt.Email, "Mã xác minh tài khoản JobPlatform",
            $"Mã OTP của bạn là {evt.Otp}. Mã có hiệu lực 5 phút, không chia sẻ mã này cho bất kỳ ai.", ct);

        db.Notifications.Add(new Data.Notification
        {
            UserId = evt.UserId,
            EventType = evt.EventType,
            Content = $"Mã xác minh đã được gửi tới {evt.Email}.",
            DeliveryStatus = "Sent"
        });
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = evt.EventId });
        await db.SaveChangesAsync(ct);
    }
}
