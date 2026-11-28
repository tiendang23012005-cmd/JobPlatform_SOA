namespace Shared.Contracts.Events;

public record MilestoneInfo(Guid MilestoneId, int Order, decimal Amount);

public record ContractCreatedEvent(Guid ContractId, Guid ApplicationId, Guid EmployerId, Guid FreelancerId,
    string Title, decimal TotalAmount) : IntegrationEvent
{ public override string EventType => "ContractCreated"; }

// Payment (lượt 5) dùng FeeRate/DepositAmount/Milestones trong sự kiện này, không đọc lại cấu hình (BR-17)
public record ContractActivatedEvent(Guid ContractId, Guid EmployerId, Guid FreelancerId, decimal TotalAmount,
    decimal FeeRate, decimal DepositAmount, List<MilestoneInfo> Milestones) : IntegrationEvent
{ public override string EventType => "ContractActivated"; }

public record MilestoneSubmittedEvent(Guid ContractId, Guid MilestoneId, Guid EmployerId, Guid FreelancerId,
    int SubmissionVersion) : IntegrationEvent
{ public override string EventType => "MilestoneSubmitted"; }

public record MilestoneApprovedEvent(Guid ContractId, Guid MilestoneId, Guid EmployerId, Guid FreelancerId,
    decimal Amount, decimal FeeRate, bool AutoApproved) : IntegrationEvent
{ public override string EventType => "MilestoneApproved"; }

public record RevisionRequestedEvent(Guid ContractId, Guid MilestoneId, Guid EmployerId, Guid FreelancerId,
    string Reason, int RevisionCount) : IntegrationEvent
{ public override string EventType => "RevisionRequested"; }
