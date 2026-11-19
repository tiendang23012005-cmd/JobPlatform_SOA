namespace Shared.Contracts.Events;

public record UserRegisteredEvent(Guid UserId, string Email, string Otp) : IntegrationEvent
{
    public override string EventType => "UserRegistered";
}
