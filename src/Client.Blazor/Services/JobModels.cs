using System.ComponentModel.DataAnnotations;

namespace Client.Blazor.Services;

public class JobItem
{
    public Guid Id { get; set; }
    public Guid EmployerId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Skills { get; set; } = "";
    public decimal BudgetMin { get; set; }
    public decimal BudgetMax { get; set; }
    public string Status { get; set; } = "";
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ApplicationItem
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid FreelancerId { get; set; }
    public string JobTitle { get; set; } = "";
    public string CoverLetter { get; set; } = "";
    public decimal ProposedBudget { get; set; }
    public string Status { get; set; } = "";
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class JobForm
{
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề.")]
    [StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mô tả.")]
    [StringLength(5000, MinimumLength = 10)]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập kỹ năng.")]
    [StringLength(1000)]
    public string Skills { get; set; } = "";

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal BudgetMin { get; set; }

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal BudgetMax { get; set; }
}

public class ApplicationForm
{
    [Required(ErrorMessage = "Vui lòng nhập thư ứng tuyển.")]
    [StringLength(5000, MinimumLength = 10)]
    public string CoverLetter { get; set; } = "";

    [Range(typeof(decimal), "1", "999999999999")]
    public decimal ProposedBudget { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public static class JobUi
{
    public static string StatusLabel(string status)
        => status switch
        {
            "Pending" => "Chờ xét duyệt",
            "Approved" => "Đã duyệt",
            "Rejected" => "Đã từ chối",
            "Closed" => "Đã đóng",
            "Accepted" => "Đã chấp nhận",
            _ => status
        };

    public static string StatusClass(string status)
        => status switch
        {
            "Pending" => "bg-warning text-dark",
            "Approved" => "bg-success",
            "Accepted" => "bg-success",
            "Rejected" => "bg-danger",
            "Closed" => "bg-secondary",
            _ => "bg-secondary"
        };

    public static string Money(decimal value)
        => value.ToString("N0",
            System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))
            + " đ";
}