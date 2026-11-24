using System.ComponentModel.DataAnnotations;

namespace Job.Api.Dtos;

public class CreateJobRequest
{
    [Required]
    [StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(5000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Skills { get; set; } = string.Empty;

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal BudgetMin { get; set; }

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal BudgetMax { get; set; }
}

public class UpdateJobRequest : CreateJobRequest
{
}

public class RejectJobRequest
{
    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public class JobResponse
{
    public Guid Id { get; set; }

    public Guid EmployerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Skills { get; set; } = string.Empty;

    public decimal BudgetMin { get; set; }

    public decimal BudgetMax { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class JobSearchRequest
{
    public string? Keyword { get; set; }

    public string? Skills { get; set; }

    public decimal? MinBudget { get; set; }

    public decimal? MaxBudget { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 10;
}

public class PagedResponse<T>
{
    public List<T> Items { get; set; } = new();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages =>
        PageSize > 0
            ? (int)Math.Ceiling(
                (double)TotalCount / PageSize)
            : 0;
}