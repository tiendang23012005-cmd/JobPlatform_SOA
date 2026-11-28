using Shared.Infrastructure.Outbox;
namespace Contract.Api.Data;

public enum ContractStatus { Draft, Active, Completed, Cancelled, Expired }
public enum MilestoneStatus { Pending, Funded, InProgress, Submitted, RevisionRequested, Approved, Released }

public class AcceptedApplication            // bản chiếu từ ApplicationAccepted
{
    public Guid ApplicationId { get; set; }  // PK
    public Guid JobId { get; set; }
    public Guid EmployerId { get; set; }
    public Guid FreelancerId { get; set; }
    public string JobTitle { get; set; } = "";
    public decimal ProposedBudget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ContractEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }  // unique
    public Guid JobId { get; set; }
    public Guid EmployerId { get; set; }
    public Guid FreelancerId { get; set; }
    public string Title { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Deliverables { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ProgressIntervalDays { get; set; } = 7;
    public int RevisionLimit { get; set; }
    public decimal FeeRate { get; set; }       // BR-06: lưu theo hợp đồng, không hồi tố
    public decimal DepositRate { get; set; }
    public decimal DepositAmount { get; set; }
    public bool DepositHeld { get; set; }      // lượt 5: Payment báo DepositHeld
    public bool InDispute { get; set; }        // lượt sau: tranh chấp -> chặn tự nghiệm thu
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public int CurrentVersion { get; set; } = 1;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastProgressAt { get; set; }
    public List<Milestone> Milestones { get; set; } = [];
    public List<ContractVersion> Versions { get; set; } = [];
}

public class ContractVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public int Version { get; set; }
    public string Snapshot { get; set; } = "";   // JSON điều khoản + milestone tại thời điểm đó
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EmployerConfirmedAt { get; set; }
    public DateTime? FreelancerConfirmedAt { get; set; }
}

public class Milestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public ContractEntity Contract { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Pending;
    public int ProgressPercent { get; set; }
    public int RevisionCount { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool AutoApproved { get; set; }
    public List<ProgressUpdate> Updates { get; set; } = [];
    public List<Submission> Submissions { get; set; } = [];
}

public class ProgressUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MilestoneId { get; set; }
    public int Percent { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MilestoneId { get; set; }
    public int Version { get; set; }
    public string Note { get; set; } = "";
    public string ArtifactUrl { get; set; } = "";
    public bool IsLate { get; set; }
    public string? RevisionReason { get; set; }  // lý do Nhà tuyển dụng yêu cầu sửa bản này
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
