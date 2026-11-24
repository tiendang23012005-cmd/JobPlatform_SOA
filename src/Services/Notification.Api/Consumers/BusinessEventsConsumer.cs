
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;
using Shared.Contracts.Events;
using Shared.Infrastructure.Messaging;

namespace Notification.Api.Consumers;

// Thông báo khi công việc được duyệt
public class JobApprovedConsumer(
    RabbitMqConnection mq,
    IServiceScopeFactory scopes,
    ILogger<JobApprovedConsumer> logger)
    : EventConsumer<JobApprovedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.job-approved";
    protected override string RoutingKey => "JobApproved";

    protected override Task HandleAsync(
        JobApprovedEvent evt, IServiceProvider sp, CancellationToken ct)
        => NotificationHelper.SaveAsync(
            evt.EventId, evt.EmployerId, evt.EventType,
            $"Công việc '{evt.Title}' đã được duyệt.", sp, ct);
}

// Thông báo khi freelancer gửi đơn ứng tuyển
public class ApplicationSubmittedConsumer(
    RabbitMqConnection mq,
    IServiceScopeFactory scopes,
    ILogger<ApplicationSubmittedConsumer> logger)
    : EventConsumer<ApplicationSubmittedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.application-submitted";
    protected override string RoutingKey => "ApplicationSubmitted";

    protected override Task HandleAsync(
        ApplicationSubmittedEvent evt, IServiceProvider sp, CancellationToken ct)
        => NotificationHelper.SaveAsync(
            evt.EventId, evt.EmployerId, evt.EventType,
            $"Có đơn ứng tuyển mới cho công việc '{evt.JobTitle}'.", sp, ct);
}

// Thông báo khi đơn ứng tuyển được chấp nhận
public class ApplicationAcceptedConsumer(
    RabbitMqConnection mq,
    IServiceScopeFactory scopes,
    ILogger<ApplicationAcceptedConsumer> logger)
    : EventConsumer<ApplicationAcceptedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.application-accepted";
    protected override string RoutingKey => "ApplicationAccepted";

    protected override async Task HandleAsync(
        ApplicationAcceptedEvent evt, IServiceProvider sp, CancellationToken ct)
    {
        await NotificationHelper.SaveAsync(
            evt.EventId, evt.FreelancerId, evt.EventType,
            $"Đơn ứng tuyển vào '{evt.JobTitle}' đã được chấp nhận.", sp, ct);
    }
}

// Thông báo khi đơn ứng tuyển bị từ chối
public class ApplicationRejectedConsumer(
    RabbitMqConnection mq,
    IServiceScopeFactory scopes,
    ILogger<ApplicationRejectedConsumer> logger)
    : EventConsumer<ApplicationRejectedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.application-rejected";
    protected override string RoutingKey => "ApplicationRejected";

    protected override Task HandleAsync(
        ApplicationRejectedEvent evt, IServiceProvider sp, CancellationToken ct)
        => NotificationHelper.SaveAsync(
            evt.EventId, evt.FreelancerId, evt.EventType,
            $"Đơn ứng tuyển vào '{evt.JobTitle}' đã bị từ chối. Lý do: {evt.Reason}",
            sp, ct);
}

// Thông báo khi nạp ví thành công
public class WalletTopUpCompletedConsumer(
    RabbitMqConnection mq,
    IServiceScopeFactory scopes,
    ILogger<WalletTopUpCompletedConsumer> logger)
    : EventConsumer<WalletTopUpCompletedEvent>(mq, scopes, logger)
{
    protected override string QueueName => "notification.wallet-topup-completed";
    protected override string RoutingKey => "WalletTopUpCompleted";

    protected override Task HandleAsync(
        WalletTopUpCompletedEvent evt, IServiceProvider sp, CancellationToken ct)
        => NotificationHelper.SaveAsync(
            evt.EventId, evt.UserId, evt.EventType,
            $"Bạn đã nạp thành công {evt.Amount:N0} vào ví.", sp, ct);
}

// Hàm dùng chung để lưu thông báo và chống xử lý trùng sự kiện
internal static class NotificationHelper
{
    public static async Task SaveAsync(
        Guid eventId,
        Guid userId,
        string eventType,
        string content,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<NotificationDbContext>();

        if (await db.ProcessedEvents.AnyAsync(
            x => x.EventId == eventId, ct))
            return;

        db.Notifications.Add(new Notification.Api.Data.Notification
        {
            UserId = userId,
            EventType = eventType,
            Content = content,
            DeliveryStatus = "Sent"
        });

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId
        });

        await db.SaveChangesAsync(ct);
    }
}
