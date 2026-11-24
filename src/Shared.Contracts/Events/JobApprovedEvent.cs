namespace Shared.Contracts.Events;

public record JobApprovedEvent(
    Guid JobId,
    Guid EmployerId,
    string Title
) : IntegrationEvent
{
    public override string EventType => "JobApproved";
}