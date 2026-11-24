using System.ComponentModel.DataAnnotations;

namespace Job.Api.Dtos;

public class CreateApplicationRequest
{
    [Required]
    [StringLength(5000, MinimumLength = 10)]
    public string CoverLetter { get; set; }
        = string.Empty;

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal ProposedBudget { get; set; }
}

public class RejectApplicationRequest
{
    [Required]
    [StringLength(1000)]
    public string Reason { get; set; }
        = string.Empty;
}

public class ApplicationResponse
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string JobTitle { get; set; }
        = string.Empty;

    public Guid FreelancerId { get; set; }

    public string CoverLetter { get; set; }
        = string.Empty;

    public decimal ProposedBudget { get; set; }

    public string Status { get; set; }
        = string.Empty;

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}