using Job.Api.Enums;

namespace Job.Api.Entities;

public class Application
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    public Guid FreelancerId { get; set; }

    public string CoverLetter { get; set; }
        = string.Empty;

    public decimal ProposedBudget { get; set; }

    public ApplicationStatus Status { get; set; }
        = ApplicationStatus.Pending;

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public Job Job { get; set; } = null!;
}