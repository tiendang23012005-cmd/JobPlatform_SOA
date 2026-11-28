using System.Security.Claims;
using Contract.Api.Data;
using Contract.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Events;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Outbox;
namespace Contract.Api.Controllers;

public record ProgressRequest(int Percent, string? Note);
public record SubmitRequest(string ArtifactUrl, string? Note);
public record RevisionRequest(string Reason);

[ApiController, Authorize, Route("api/v1/milestones")]
public class MilestonesController(ContractDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue("sub")!);
    private static readonly MilestoneStatus[] Workable =
        [MilestoneStatus.Funded, MilestoneStatus.InProgress, MilestoneStatus.RevisionRequested];

    // UC-07 A1: lưu tiến độ (BR-09)
    [HttpPost("{id:guid}/progress"), Authorize(Policy = Policies.FreelancerOnly)]
    public async Task<IActionResult> Progress(Guid id, ProgressRequest r, CancellationToken ct)
    {
        var (m, err) = await LoadForFreelancer(id, ct); if (err != null) return err;
        if (r.Percent is < 0 or > 100) return BadRequest(new { error = "Tiến độ phải từ 0 đến 100." });
        m!.ProgressPercent = r.Percent;
        if (m.Status is MilestoneStatus.Funded or MilestoneStatus.RevisionRequested) m.Status = MilestoneStatus.InProgress;
        m.Updates.Add(new ProgressUpdate { Percent = r.Percent, Note = r.Note ?? "" });
        m.Contract.LastProgressAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { m.Id, m.Status, m.ProgressPercent });
    }

    // UC-07 luồng chính: nộp sản phẩm
    [HttpPost("{id:guid}/submit"), Authorize(Policy = Policies.FreelancerOnly)]
    public async Task<IActionResult> Submit(Guid id, SubmitRequest r, CancellationToken ct)
    {
        var (m, err) = await LoadForFreelancer(id, ct); if (err != null) return err;
        if (!Uri.TryCreate(r.ArtifactUrl, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            return BadRequest(new { error = "Đường dẫn sản phẩm phải là http(s)." });

        var c = m!.Contract;
        m.Submissions.Add(new Submission
        {
            Version = m.Submissions.Count + 1, Note = r.Note ?? "", ArtifactUrl = r.ArtifactUrl,
            IsLate = DateTime.UtcNow > m.DueDate                       // E4: vẫn nhận, đánh dấu Late
        });
        m.Status = MilestoneStatus.Submitted; m.ProgressPercent = 100; m.SubmittedAt = DateTime.UtcNow;
        c.LastProgressAt = DateTime.UtcNow;
        db.AddOutbox(new MilestoneSubmittedEvent(c.Id, m.Id, c.EmployerId, c.FreelancerId, m.Submissions.Count));
        await db.SaveChangesAsync(ct);
        return Ok(new { m.Id, m.Status });
    }

    [HttpPost("{id:guid}/approve"), Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (m, err) = await LoadForEmployer(id, ct); if (err != null) return err;
        ContractDomain.Approve(db, m!, m!.Contract, auto: false);
        await db.SaveChangesAsync(ct);
        return Ok(new { m.Id, m.Status });
    }

    // BR-10: phải nêu rõ phần chưa đạt, không vượt RevisionLimit
    [HttpPost("{id:guid}/request-revision"), Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> RequestRevision(Guid id, RevisionRequest r, CancellationToken ct)
    {
        var (m, err) = await LoadForEmployer(id, ct); if (err != null) return err;
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 10)
            return BadRequest(new { error = "Hãy nêu cụ thể phần chưa đạt (tối thiểu 10 ký tự)." });
        var c = m!.Contract;
        if (m.RevisionCount >= c.RevisionLimit)
            return Conflict(new { error = "Đã hết số lần chỉnh sửa đã thỏa thuận. Hãy lập phụ lục hoặc mở tranh chấp." });

        m.RevisionCount++;
        m.Status = MilestoneStatus.RevisionRequested;
        m.Submissions.OrderBy(s => s.Version).Last().RevisionReason = r.Reason.Trim();
        db.AddOutbox(new RevisionRequestedEvent(c.Id, m.Id, c.EmployerId, c.FreelancerId, r.Reason.Trim(), m.RevisionCount));
        await db.SaveChangesAsync(ct);
        return Ok(new { m.Id, m.Status, m.RevisionCount });
    }

    private async Task<(Milestone?, IActionResult?)> LoadForFreelancer(Guid id, CancellationToken ct)
    {
        var m = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || m.Contract.FreelancerId != Me) return (null, NotFound(new { error = "Không tìm thấy milestone." })); // E2
        if (m.Contract.Status != ContractStatus.Active) return (null, Conflict(new { error = "Hợp đồng chưa Active." }));
        if (!m.Contract.DepositHeld || !Workable.Contains(m.Status))
            return (null, Conflict(new { error = "Milestone chưa Funded, Freelancer chưa đặt cọc hoặc milestone đã đóng." })); // E1/E3
        return (m, null);
    }

    private async Task<(Milestone?, IActionResult?)> LoadForEmployer(Guid id, CancellationToken ct)
    {
        var m = await Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null || m.Contract.EmployerId != Me) return (null, NotFound(new { error = "Không tìm thấy milestone." }));
        if (m.Status != MilestoneStatus.Submitted) return (null, Conflict(new { error = "Milestone không ở trạng thái Submitted." })); // E1
        return (m, null);
    }

    private IQueryable<Milestone> Query() => db.Milestones.Include(x => x.Contract).Include(x => x.Submissions).Include(x => x.Updates);
}
