using Job.Api.Enums;

namespace Job.Api.Entities;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EmployerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Skills { get; set; } = string.Empty;

    public decimal BudgetMin { get; set; }

    public decimal BudgetMax { get; set; }

    public JobStatus Status { get; set; }
        = JobStatus.Pending;

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<Application> Applications { get; set; }
        = new List<Application>();
}