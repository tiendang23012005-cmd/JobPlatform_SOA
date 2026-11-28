using System.Security.Claims;
using Contract.Api.Data;
using Contract.Api.Options;
using Contract.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Contracts.Events;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Outbox;
namespace Contract.Api.Controllers;

public record MilestoneInput(string Title, decimal Amount, DateTime DueDate);
public record ContractRequest(Guid ApplicationId, string Scope, string Deliverables, string AcceptanceCriteria,
    decimal TotalAmount, DateTime StartDate, DateTime EndDate, int ProgressIntervalDays, int? RevisionLimit,
    decimal DepositRate, List<MilestoneInput> Milestones);
public record ConfirmRequest(int Version);

[ApiController, Authorize, Route("api/v1/contracts")]
public class ContractsController(ContractDbContext db, IOptionsMonitor<ContractPolicyOptions> opt) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue("sub")!);

    // Danh sách đơn Accepted chưa có hợp đồng (cho trang "Tạo hợp đồng")
    [HttpGet("accepted-applications"), Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> AcceptedApps(CancellationToken ct) =>
        Ok(await db.AcceptedApplications
            .Where(a => a.EmployerId == Me && !db.Contracts.Any(c => c.ApplicationId == a.ApplicationId))
            .OrderByDescending(a => a.CreatedAt).ToListAsync(ct));

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Ok(await db.Contracts.Where(c => c.EmployerId == Me || c.FreelancerId == Me)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Title, c.TotalAmount, c.Status, c.CurrentVersion, c.EmployerId, c.FreelancerId, c.ExpiresAt })
            .ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var c = await Load(id, ct);
        if (c is null || (c.EmployerId != Me && c.FreelancerId != Me)) return NotFound(new { error = "Không tìm thấy hợp đồng." });
        var v = c.Versions.First(x => x.Version == c.CurrentVersion);
        return Ok(new
        {
            c.Id, c.ApplicationId, c.Title, c.Scope, c.Deliverables, c.AcceptanceCriteria, c.TotalAmount,
            c.StartDate, c.EndDate, c.ProgressIntervalDays, c.RevisionLimit, c.FeeRate, c.DepositRate,
            c.DepositAmount, c.DepositHeld, c.Status, c.CurrentVersion, c.ExpiresAt, c.EmployerId, c.FreelancerId,
            EmployerConfirmed = v.EmployerConfirmedAt != null, FreelancerConfirmed = v.FreelancerConfirmedAt != null,
            Milestones = c.Milestones.OrderBy(m => m.Order).Select(m => new
            {
                m.Id, m.Order, m.Title, m.Amount, m.DueDate, m.Status, m.ProgressPercent, m.RevisionCount,
                m.SubmittedAt, m.AutoApproved,
                Submissions = m.Submissions.OrderBy(s => s.Version).Select(s =>
                    new { s.Version, s.Note, s.ArtifactUrl, s.IsLate, s.RevisionReason, s.SubmittedAt }),
                Updates = m.Updates.OrderByDescending(u => u.CreatedAt).Take(10)
                    .Select(u => new { u.Percent, u.Note, u.CreatedAt })
            })
        });
    }

    // UC-05 bước 1-3: Nhà tuyển dụng lập hợp đồng Draft v1
    [HttpPost, Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> Create(ContractRequest r, CancellationToken ct)
    {
        var o = opt.CurrentValue;
        var app = await db.AcceptedApplications.FirstOrDefaultAsync(a => a.ApplicationId == r.ApplicationId, ct);
        if (app is null || app.EmployerId != Me)
            return BadRequest(new { error = "Đơn chưa ở trạng thái Accepted hoặc không thuộc tin của bạn." });
        if (await db.Contracts.AnyAsync(c => c.ApplicationId == r.ApplicationId, ct))
            return Conflict(new { error = "Đơn này đã có hợp đồng." });

        var err = ContractDomain.Validate(r.Scope, r.Deliverables, r.AcceptanceCriteria, r.TotalAmount, r.StartDate,
            r.EndDate, r.DepositRate, r.Milestones.Select(m => (m.Amount, m.DueDate, m.Title)).ToList(), o, out var deposit);
        if (err != null) return BadRequest(new { error = err });

        var c = new ContractEntity
        {
            ApplicationId = app.ApplicationId, JobId = app.JobId, EmployerId = app.EmployerId,
            FreelancerId = app.FreelancerId, Title = app.JobTitle,
            Scope = r.Scope, Deliverables = r.Deliverables, AcceptanceCriteria = r.AcceptanceCriteria,
            TotalAmount = r.TotalAmount, StartDate = r.StartDate, EndDate = r.EndDate,
            ProgressIntervalDays = Math.Max(1, r.ProgressIntervalDays),
            RevisionLimit = r.RevisionLimit ?? o.DefaultRevisionLimit,
            FeeRate = Math.Clamp(o.FeeRate, o.MinFeeRate, o.MaxFeeRate),   // BR-17
            DepositRate = r.DepositRate, DepositAmount = deposit,
            ExpiresAt = DateTime.UtcNow.AddDays(o.SignExpiryDays)
        };
        c.Milestones = r.Milestones.Select((m, i) => new Milestone { Order = i + 1, Title = m.Title, Amount = m.Amount, DueDate = m.DueDate }).ToList();
        c.Versions.Add(new ContractVersion { Version = 1, CreatedBy = Me, Snapshot = ContractDomain.BuildSnapshot(c) });

        db.Contracts.Add(c);
        db.AddOutbox(new ContractCreatedEvent(c.Id, c.ApplicationId, c.EmployerId, c.FreelancerId, c.Title, c.TotalAmount));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "Đơn này đã có hợp đồng." }); } // đụng unique index
        return CreatedAtAction(nameof(Get), new { id = c.Id }, new { c.Id, c.Status, c.CurrentVersion, c.FeeRate, c.DepositAmount });
    }

    // Sửa điều khoản khi còn Draft -> phiên bản mới, xác nhận cũ mất hiệu lực (A1, BR-07)
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> Revise(Guid id, ContractRequest r, CancellationToken ct)
    {
        var c = await Load(id, ct);
        if (c is null || c.EmployerId != Me) return NotFound(new { error = "Không tìm thấy hợp đồng." });
        if (c.Status != ContractStatus.Draft) return Conflict(new { error = "Chỉ sửa trực tiếp khi hợp đồng còn Draft (đã ký thì lập phụ lục)." });

        var o = opt.CurrentValue;
        var err = ContractDomain.Validate(r.Scope, r.Deliverables, r.AcceptanceCriteria, r.TotalAmount, r.StartDate,
            r.EndDate, r.DepositRate, r.Milestones.Select(m => (m.Amount, m.DueDate, m.Title)).ToList(), o, out var deposit);
        if (err != null) return BadRequest(new { error = err });

        db.Milestones.RemoveRange(c.Milestones);            // Draft: chưa có tiền nên thay mới an toàn
        c.Milestones = r.Milestones.Select((m, i) => new Milestone { Order = i + 1, Title = m.Title, Amount = m.Amount, DueDate = m.DueDate }).ToList();
        (c.Scope, c.Deliverables, c.AcceptanceCriteria, c.TotalAmount) = (r.Scope, r.Deliverables, r.AcceptanceCriteria, r.TotalAmount);
        (c.StartDate, c.EndDate, c.DepositRate, c.DepositAmount) = (r.StartDate, r.EndDate, r.DepositRate, deposit);
        c.ProgressIntervalDays = Math.Max(1, r.ProgressIntervalDays);
        c.RevisionLimit = r.RevisionLimit ?? c.RevisionLimit;
        c.CurrentVersion++;
        c.ExpiresAt = DateTime.UtcNow.AddDays(o.SignExpiryDays);
        c.Versions.Add(new ContractVersion { Version = c.CurrentVersion, CreatedBy = Me, Snapshot = ContractDomain.BuildSnapshot(c) });
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id, c.CurrentVersion });
    }

    // UC-05 bước 4-6: mỗi bên xác nhận ĐÚNG phiên bản hiện hành; đủ 2 bên -> Active
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmRequest r, CancellationToken ct)
    {
        var c = await Load(id, ct);
        if (c is null || (c.EmployerId != Me && c.FreelancerId != Me)) return NotFound(new { error = "Không tìm thấy hợp đồng." });
        if (c.Status == ContractStatus.Draft && c.ExpiresAt <= DateTime.UtcNow)
        { c.Status = ContractStatus.Expired; await db.SaveChangesAsync(ct); }
        if (c.Status != ContractStatus.Draft) return Conflict(new { error = $"Hợp đồng đang ở trạng thái {c.Status}." });
        if (r.Version != c.CurrentVersion) return Conflict(new { error = "Hợp đồng đã có phiên bản mới, hãy xem lại rồi xác nhận." });

        var v = c.Versions.First(x => x.Version == c.CurrentVersion);
        if (c.EmployerId == Me) v.EmployerConfirmedAt ??= DateTime.UtcNow; else v.FreelancerConfirmedAt ??= DateTime.UtcNow;

        if (v.EmployerConfirmedAt != null && v.FreelancerConfirmedAt != null)
        {
            c.Status = ContractStatus.Active;
            db.AddOutbox(new ContractActivatedEvent(c.Id, c.EmployerId, c.FreelancerId, c.TotalAmount, c.FeeRate,
                c.DepositAmount, c.Milestones.OrderBy(m => m.Order).Select(m => new MilestoneInfo(m.Id, m.Order, m.Amount)).ToList()));
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id, c.Status });
    }

    // A2: một bên từ chối -> Cancelled
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var c = await Load(id, ct);
        if (c is null || (c.EmployerId != Me && c.FreelancerId != Me)) return NotFound(new { error = "Không tìm thấy hợp đồng." });
        if (c.Status != ContractStatus.Draft) return Conflict(new { error = "Chỉ từ chối được hợp đồng Draft." });
        c.Status = ContractStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id, c.Status });
    }

    private Task<ContractEntity?> Load(Guid id, CancellationToken ct) =>
        db.Contracts.Include(c => c.Versions).Include(c => c.Milestones).ThenInclude(m => m.Submissions)
            .Include(c => c.Milestones).ThenInclude(m => m.Updates)
            .AsSplitQuery().FirstOrDefaultAsync(c => c.Id == id, ct);
}
