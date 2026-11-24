using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Job.Api.Data;
using Job.Api.Dtos;
using Job.Api.Enums;
using Job.Api.Services;
using Shared.Contracts.Events;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Outbox;
using JobEntity = Job.Api.Entities.Job;

namespace Job.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
[Authorize]
public class JobsController(
    JobDbContext db,
    IdentityClient identity) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue("sub")!);

    // 1. Employer đăng tin tuyển dụng
    [HttpPost]
    [Authorize(Policy = Policies.EmployerOnly)]
    [Authorize(Policy = Policies.VerifiedUser)]
    public async Task<IActionResult> Create(
        CreateJobRequest request,
        CancellationToken ct)
    {
        if (request.BudgetMin > request.BudgetMax)
        {
            return BadRequest(new
            {
                error = "Ngân sách tối thiểu không được lớn hơn ngân sách tối đa."
            });
        }

        if (!await identity.IsActiveAsync(
            Request.Headers["Authorization"].ToString(), ct))
        {
            return StatusCode(403, new
            {
                error = "Tài khoản không còn hoạt động."
            });
        }

        var job = new JobEntity
        {
            EmployerId = CurrentUserId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Skills = request.Skills.Trim(),
            BudgetMin = request.BudgetMin,
            BudgetMax = request.BudgetMax,
            Status = JobStatus.Pending
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = job.Id },
            ToResponse(job));
    }

    // 2. Employer chỉnh sửa tin
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.EmployerOnly)]
    [Authorize(Policy = Policies.VerifiedUser)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateJobRequest request,
        CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync(
            new object[] { id }, ct);

        if (job is null)
            return NotFound(new
            {
                error = "Không tìm thấy tin tuyển dụng."
            });

        if (job.EmployerId != CurrentUserId)
            return Forbid();

        if (job.Status == JobStatus.Closed)
            return BadRequest(new
            {
                error = "Không thể chỉnh sửa tin đã đóng."
            });

        if (request.BudgetMin > request.BudgetMax)
            return BadRequest(new
            {
                error = "Ngân sách không hợp lệ."
            });

        if (!await identity.IsActiveAsync(
            Request.Headers["Authorization"].ToString(), ct))
            return StatusCode(403, new
            {
                error = "Tài khoản không còn hoạt động."
            });

        job.Title = request.Title.Trim();
        job.Description = request.Description.Trim();
        job.Skills = request.Skills.Trim();
        job.BudgetMin = request.BudgetMin;
        job.BudgetMax = request.BudgetMax;

        // Tin chỉnh sửa phải được xét duyệt lại
        job.Status = JobStatus.Pending;
        job.RejectionReason = null;
        job.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(ToResponse(job));
    }

    // 3. Employer đóng tin
    [HttpPut("{id:guid}/close")]
    [Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> Close(
        Guid id,
        CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync(
            new object[] { id }, ct);

        if (job is null)
            return NotFound(new
            {
                error = "Không tìm thấy tin tuyển dụng."
            });

        if (job.EmployerId != CurrentUserId)
            return Forbid();

        if (!await identity.IsActiveAsync(
            Request.Headers["Authorization"].ToString(), ct))
            return StatusCode(403, new
            {
                error = "Tài khoản không còn hoạt động."
            });

        if (job.Status == JobStatus.Closed)
            return Ok(ToResponse(job));

        job.Status = JobStatus.Closed;
        job.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(ToResponse(job));
    }

    // 4. Employer xem các tin của mình
    [HttpGet("mine")]
    [Authorize(Policy = Policies.EmployerOnly)]
    public async Task<IActionResult> Mine(
        CancellationToken ct)
    {
        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(x => x.EmployerId == CurrentUserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return Ok(jobs.Select(ToResponse));
    }

    // 5. Tìm kiếm, lọc và phân trang
    [HttpGet]
    [Authorize(Policy = Policies.VerifiedUser)]
    public async Task<IActionResult> Search(
        [FromQuery] JobSearchRequest request,
        CancellationToken ct)
    {
        if (request.MinBudget < 0 ||
            request.MaxBudget < 0 ||
            request.MinBudget > request.MaxBudget)
        {
            return BadRequest(new
            {
                error = "Khoảng ngân sách không hợp lệ."
            });
        }

        var query = db.Jobs
            .AsNoTracking()
            .Where(x => x.Status == JobStatus.Approved);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            query = query.Where(x =>
                x.Title.Contains(keyword) ||
                x.Description.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Skills))
        {
            var skills = request.Skills.Trim();

            query = query.Where(x =>
                x.Skills.Contains(skills));
        }

        // Lọc các tin có khoảng ngân sách giao nhau
        // với khoảng ngân sách người dùng tìm kiếm.
        if (request.MinBudget.HasValue)
        {
            query = query.Where(x =>
                x.BudgetMax >= request.MinBudget.Value);
        }

        if (request.MaxBudget.HasValue)
        {
            query = query.Where(x =>
                x.BudgetMin <= request.MaxBudget.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var offset = (long)(request.Page - 1)
                     * request.PageSize;

        if (offset > int.MaxValue)
        {
            return BadRequest(new
            {
                error = "Số trang vượt quá giới hạn."
            });
        }

        var jobs = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((int)offset)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(new PagedResponse<JobResponse>
        {
            Items = jobs.Select(ToResponse).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    // 6. Xem chi tiết tin tuyển dụng
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (job is null)
            return NotFound(new
            {
                error = "Không tìm thấy tin tuyển dụng."
            });

        var isAdmin = User.IsInRole("Admin");
        var isOwner = job.EmployerId == CurrentUserId;

        if (job.Status != JobStatus.Approved &&
            !isAdmin && !isOwner)
        {
            return NotFound(new
            {
                error = "Tin tuyển dụng không khả dụng."
            });
        }

        return Ok(ToResponse(job));
    }

    // 7. Admin xem các tin chờ duyệt
    [HttpGet("pending")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Pending(
        CancellationToken ct)
    {
        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(x => x.Status == JobStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return Ok(jobs.Select(ToResponse));
    }

    // 8. Admin duyệt tin
    [HttpPut("{id:guid}/approve")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync(
            new object[] { id }, ct);

        if (job is null)
            return NotFound(new
            {
                error = "Không tìm thấy tin tuyển dụng."
            });

        if (job.Status != JobStatus.Pending)
            return BadRequest(new
            {
                error = "Chỉ được duyệt tin đang chờ duyệt."
            });

        job.Status = JobStatus.Approved;
        job.RejectionReason = null;
        job.UpdatedAt = DateTime.UtcNow;

        // Ghi sự kiện vào Outbox
        db.AddOutbox(new JobApprovedEvent(
            job.Id,
            job.EmployerId,
            job.Title));

        // Dữ liệu Job và Outbox được lưu cùng nhau
        await db.SaveChangesAsync(ct);

        return Ok(ToResponse(job));
    }

    // 9. Admin từ chối tin
    [HttpPut("{id:guid}/reject")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Reject(
        Guid id,
        RejectJobRequest request,
        CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync(
            new object[] { id }, ct);

        if (job is null)
            return NotFound(new
            {
                error = "Không tìm thấy tin tuyển dụng."
            });

        if (job.Status != JobStatus.Pending)
            return BadRequest(new
            {
                error = "Chỉ được từ chối tin đang chờ duyệt."
            });

        job.Status = JobStatus.Rejected;
        job.RejectionReason = request.Reason.Trim();
        job.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(ToResponse(job));
    }

    // Chuyển Entity sang DTO
    private static JobResponse ToResponse(JobEntity job)
    {
        return new JobResponse
        {
            Id = job.Id,
            EmployerId = job.EmployerId,
            Title = job.Title,
            Description = job.Description,
            Skills = job.Skills,
            BudgetMin = job.BudgetMin,
            BudgetMax = job.BudgetMax,
            Status = job.Status.ToString(),
            RejectionReason = job.RejectionReason,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt
        };
    }
}