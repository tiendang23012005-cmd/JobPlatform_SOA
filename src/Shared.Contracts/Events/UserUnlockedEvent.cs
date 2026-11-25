namespace Shared.Contracts.Events;

/// <summary>Phát khi Admin mở khóa một tài khoản người dùng.</summary>
public sealed record UserUnlockedEvent(Guid UserId, Guid AdminId) : IntegrationEvent
{
    public override string EventType => "UserUnlocked";
}
