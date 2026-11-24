namespace Shared.Contracts.Events;

public record ApplicationSubmittedEvent(
    Guid ApplicationId,
    Guid JobId,
    Guid EmployerId,
    Guid FreelancerId,
    string JobTitle,
    decimal ProposedBudget
) : IntegrationEvent
{
    public override string EventType
        => "ApplicationSubmitted";
}