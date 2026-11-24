using System.Security.Claims;
using System.Data;
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
using ApplicationEntity = Job.Api.Entities.Application;

namespace Job.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public class ApplicationsController(
	JobDbContext db,
	IdentityClient identity) : ControllerBase
{
	private Guid CurrentUserId =>
		Guid.Parse(User.FindFirstValue("sub")!);

	// =====================================
	// 1. FREELANCER GỬI ĐƠN ỨNG TUYỂN
	// =====================================
	[HttpPost("jobs/{jobId:guid}/applications")]
	[Authorize(Policy = Policies.FreelancerOnly)]
	[Authorize(Policy = Policies.VerifiedUser)]
	public async Task<IActionResult> Submit(
		Guid jobId,
		CreateApplicationRequest request,
		CancellationToken ct)
	{
		if (!await identity.IsActiveAsync(
			Request.Headers["Authorization"].ToString(), ct))
		{
			return StatusCode(403, new
			{
				error = "Tài khoản không còn hoạt động."
			});
		}

		await using var transaction =
			await db.Database.BeginTransactionAsync(
				IsolationLevel.Serializable, ct);

		var job = await db.Jobs
			.FirstOrDefaultAsync(x => x.Id == jobId, ct);

		if (job is null)
		{
			return NotFound(new
			{
				error = "Không tìm thấy tin tuyển dụng."
			});
		}

		if (job.Status != JobStatus.Approved)
		{
			return BadRequest(new
			{
				error = "Tin tuyển dụng không còn khả dụng."
			});
		}

		if (job.EmployerId == CurrentUserId)
		{
			return BadRequest(new
			{
				error = "Không thể ứng tuyển vào tin của chính mình."
			});
		}

		if (request.ProposedBudget < job.BudgetMin ||
			request.ProposedBudget > job.BudgetMax)
		{
			return BadRequest(new
			{
				error = "Ngân sách đề xuất phải nằm trong khoảng ngân sách của tin tuyển dụng."
			});
		}

		var exists = await db.Applications.AnyAsync(
			x => x.JobId == jobId &&
				 x.FreelancerId == CurrentUserId, ct);

		if (exists)
		{
			return Conflict(new
			{
				error = "Bạn đã ứng tuyển vào tin này."
			});
		}

		var application = new ApplicationEntity
		{
			JobId = jobId,
			Job = job,
			FreelancerId = CurrentUserId,
			CoverLetter = request.CoverLetter.Trim(),
			ProposedBudget = request.ProposedBudget,
			Status = ApplicationStatus.Pending
		};

		db.Applications.Add(application);

		db.AddOutbox(new ApplicationSubmittedEvent(
			application.Id,
			job.Id,
			job.EmployerId,
			application.FreelancerId,
			job.Title,
			application.ProposedBudget));

		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);

		return StatusCode(201, ToResponse(application));
	}

	// =====================================
	// 2. FREELANCER XEM ĐƠN CỦA MÌNH
	// =====================================
	[HttpGet("applications/mine")]
	[Authorize(Policy = Policies.FreelancerOnly)]
	public async Task<IActionResult> Mine(
		CancellationToken ct)
	{
		var applications = await db.Applications
			.AsNoTracking()
			.Include(x => x.Job)
			.Where(x =>
				x.FreelancerId == CurrentUserId)
			.OrderByDescending(x => x.CreatedAt)
			.ToListAsync(ct);

		return Ok(applications.Select(ToResponse));
	}

	// =====================================
	// 3. EMPLOYER XEM DANH SÁCH ỨNG VIÊN
	// =====================================
	[HttpGet("jobs/{jobId:guid}/applications")]
	[Authorize(Policy = Policies.EmployerOnly)]
	public async Task<IActionResult> GetApplicants(
		Guid jobId,
		CancellationToken ct)
	{
		var job = await db.Jobs
			.AsNoTracking()
			.FirstOrDefaultAsync(
				x => x.Id == jobId, ct);

		if (job is null)
		{
			return NotFound(new
			{
				error = "Không tìm thấy tin tuyển dụng."
			});
		}

		if (job.EmployerId != CurrentUserId)
			return Forbid();

		var applications = await db.Applications
			.AsNoTracking()
			.Include(x => x.Job)
			.Where(x => x.JobId == jobId)
			.OrderByDescending(x => x.CreatedAt)
			.ToListAsync(ct);

		return Ok(applications.Select(ToResponse));
	}

	// =====================================
	// 4. EMPLOYER CHẤP NHẬN ĐƠN
	// =====================================
	[HttpPut("applications/{id:guid}/accept")]
	[Authorize(Policy = Policies.EmployerOnly)]
	[Authorize(Policy = Policies.VerifiedUser)]
	public async Task<IActionResult> Accept(
		Guid id,
		CancellationToken ct)
	{
		if (!await identity.IsActiveAsync(
			Request.Headers["Authorization"].ToString(), ct))
		{
			return StatusCode(403, new
			{
				error = "Tài khoản không còn hoạt động."
			});
		}

		await using var transaction =
			await db.Database.BeginTransactionAsync(
				IsolationLevel.Serializable, ct);

		var application = await db.Applications
			.Include(x => x.Job)
			.FirstOrDefaultAsync(x => x.Id == id, ct);

		if (application is null)
		{
			return NotFound(new
			{
				error = "Không tìm thấy đơn ứng tuyển."
			});
		}

		if (application.Job.EmployerId != CurrentUserId)
			return Forbid();

		if (application.Job.Status != JobStatus.Approved)
		{
			return BadRequest(new
			{
				error = "Tin tuyển dụng không còn khả dụng."
			});
		}

		if (application.Status != ApplicationStatus.Pending)
		{
			return BadRequest(new
			{
				error = "Chỉ được chấp nhận đơn đang chờ xử lý."
			});
		}

		application.Status = ApplicationStatus.Accepted;
		application.RejectionReason = null;
		application.UpdatedAt = DateTime.UtcNow;

		db.AddOutbox(new ApplicationAcceptedEvent(
			application.Id,
			application.JobId,
			application.Job.EmployerId,
			application.FreelancerId,
			application.Job.Title,
			application.ProposedBudget));

		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);

		return Ok(ToResponse(application));
	}

	// =====================================
	// 5. EMPLOYER TỪ CHỐI ĐƠN
	// =====================================
	[HttpPut("applications/{id:guid}/reject")]
	[Authorize(Policy = Policies.EmployerOnly)]
	[Authorize(Policy = Policies.VerifiedUser)]
	public async Task<IActionResult> Reject(
		Guid id,
		RejectApplicationRequest request,
		CancellationToken ct)
	{
		if (!await identity.IsActiveAsync(
			Request.Headers["Authorization"].ToString(), ct))
		{
			return StatusCode(403, new
			{
				error = "Tài khoản không còn hoạt động."
			});
		}

		await using var transaction =
			await db.Database.BeginTransactionAsync(
				IsolationLevel.Serializable, ct);

		var application = await db.Applications
			.Include(x => x.Job)
			.FirstOrDefaultAsync(x => x.Id == id, ct);

		if (application is null)
		{
			return NotFound(new
			{
				error = "Không tìm thấy đơn ứng tuyển."
			});
		}

		if (application.Job.EmployerId != CurrentUserId)
			return Forbid();

		if (application.Status != ApplicationStatus.Pending)
		{
			return BadRequest(new
			{
				error = "Chỉ được từ chối đơn đang chờ xử lý."
			});
		}

		application.Status = ApplicationStatus.Rejected;
		application.RejectionReason = request.Reason.Trim();
		application.UpdatedAt = DateTime.UtcNow;

		db.AddOutbox(new ApplicationRejectedEvent(
			application.Id,
			application.JobId,
			application.Job.EmployerId,
			application.FreelancerId,
			application.Job.Title,
			application.RejectionReason));

		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);

		return Ok(ToResponse(application));
	}

	// =====================================
	// CHUYỂN ENTITY SANG DTO
	// =====================================
	private static ApplicationResponse ToResponse(
		ApplicationEntity application)
	{
		return new ApplicationResponse
		{
			Id = application.Id,
			JobId = application.JobId,
			JobTitle = application.Job.Title,
			FreelancerId = application.FreelancerId,
			CoverLetter = application.CoverLetter,
			ProposedBudget = application.ProposedBudget,
			Status = application.Status.ToString(),
			RejectionReason = application.RejectionReason,
			CreatedAt = application.CreatedAt,
			UpdatedAt = application.UpdatedAt
		};
	}
}