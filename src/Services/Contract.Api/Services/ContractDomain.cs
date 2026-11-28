using System.Text.Json;
using Contract.Api.Data;
using Contract.Api.Options;
using Shared.Contracts.Events;
using Shared.Infrastructure.Outbox;
namespace Contract.Api.Services;

public static class ContractDomain
{
    public static string BuildSnapshot(ContractEntity c) => JsonSerializer.Serialize(new
    {
        c.Scope, c.Deliverables, c.AcceptanceCriteria, c.TotalAmount, c.StartDate, c.EndDate,
        c.ProgressIntervalDays, c.RevisionLimit, c.FeeRate, c.DepositRate, c.DepositAmount,
        Milestones = c.Milestones.OrderBy(m => m.Order).Select(m => new { m.Title, m.Amount, m.DueDate })
    });

    /// BR-06: kiểm tra điều khoản. Trả null nếu hợp lệ.
    public static string? Validate(string scope, string deliverables, string criteria, decimal total,
        DateTime start, DateTime end, decimal depositRate, IReadOnlyList<(decimal Amount, DateTime Due, string Title)> ms,
        ContractPolicyOptions o, out decimal depositAmount)
    {
        depositAmount = 0;
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(deliverables) ||
            string.IsNullOrWhiteSpace(criteria)) return "Thiếu phạm vi, đầu ra hoặc tiêu chí nghiệm thu.";
        if (total <= 0 || end <= start) return "Giá trị hoặc thời hạn hợp đồng không hợp lệ.";
        if (ms.Count == 0) return "Hợp đồng cần ít nhất một milestone.";
        if (ms.Any(m => m.Amount <= 0 || string.IsNullOrWhiteSpace(m.Title))) return "Milestone không hợp lệ.";
        if (ms.Sum(m => m.Amount) != total) return "Tổng tiền milestone phải bằng giá trị hợp đồng.";
        if (depositRate <= 0 || depositRate >= o.MaxDepositRate) return $"Tỷ lệ cọc phải lớn hơn 0% và nhỏ hơn {o.MaxDepositRate:P0}.";
        depositAmount = Math.Min(Math.Round(depositRate * total, 0), o.MaxDepositAmount);
        return null;
    }

    /// Dùng cho nghiệm thu thủ công và tự nghiệm thu (BR-11).
    public static void Approve(ContractDbContext db, Milestone m, ContractEntity c, bool auto)
    {
        m.Status = MilestoneStatus.Approved;
        m.ApprovedAt = DateTime.UtcNow;
        m.AutoApproved = auto;
        db.AddOutbox(new MilestoneApprovedEvent(c.Id, m.Id, c.EmployerId, c.FreelancerId, m.Amount, c.FeeRate, auto));
    }

    // ===== Lượt 5 gọi lại 2 hàm này từ consumer MilestoneFunded / DepositHeld =====
    public static void MarkMilestoneFunded(Milestone m)
    { if (m.Status == MilestoneStatus.Pending) m.Status = MilestoneStatus.Funded; }
    public static void MarkDepositHeld(ContractEntity c) => c.DepositHeld = true;
}
