namespace Shared.Contracts.Events;

public record WalletTopUpCompletedEvent(Guid WalletId, Guid UserId, Guid TransactionId, Guid TopUpId, decimal Amount)
    : IntegrationEvent
{
    public override string EventType => "WalletTopUpCompleted";
}
