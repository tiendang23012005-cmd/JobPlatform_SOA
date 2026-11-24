namespace Shared.Contracts.Events;

public record ApplicationRejectedEvent(
    Guid ApplicationId,
    Guid JobId,
    Guid EmployerId,
    Guid FreelancerId,
    string JobTitle,
    string Reason
) : IntegrationEvent
{
    public override string EventType
        => "ApplicationRejected";
}