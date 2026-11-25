namespace Shared.Contracts.Events;

/// <summary>Phát khi Admin khóa một tài khoản người dùng.</summary>
public sealed record UserLockedEvent(Guid UserId, Guid AdminId) : IntegrationEvent
{
    public override string EventType => "UserLocked";
}
