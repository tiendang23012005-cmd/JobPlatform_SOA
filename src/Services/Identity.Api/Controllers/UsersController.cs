using System.Security.Claims;
using Identity.Api.Data;
using Identity.Api.Dtos;
using Identity.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Constants;
using Shared.Contracts.Events;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Outbox;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController(IdentityDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = CurrentUserId();
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound(new { error = "Không tìm thấy tài khoản." });
        return Ok(new
        {
            u.Id, u.Email, u.Role, u.Status, u.IsVerified,
            FullName = u.Profile?.FullName, Phone = u.Profile?.Phone,
            Skills = u.Profile?.Skills, Bio = u.Profile?.Bio
        });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request)
    {
        var id = CurrentUserId();
        var user = await db.Users.Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return NotFound(new { error = "Không tìm thấy tài khoản." });

        if (user.Profile is null)
            user.Profile = new Profile { UserId = user.Id };

        user.Profile.FullName = request.FullName.Trim();
        user.Profile.Phone = NormalizeOptional(request.Phone);
        user.Profile.Skills = NormalizeOptional(request.Skills);
        user.Profile.Bio = NormalizeOptional(request.Bio);
        await db.SaveChangesAsync();

        return Ok(new
        {
            message = "Cập nhật hồ sơ thành công.",
            user.Id, user.Email, user.Role, user.Status,
            user.Profile.FullName, user.Profile.Phone, user.Profile.Skills, user.Profile.Bio
        });
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == CurrentUserId());
        if (user is null) return NotFound(new { error = "Không tìm thấy tài khoản." });
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { error = "Mật khẩu hiện tại không chính xác." });
        if (!PasswordRules.IsValid(request.NewPassword))
            return BadRequest(new { error = PasswordRules.Message });
        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            return BadRequest(new { error = "Mật khẩu mới phải khác mật khẩu hiện tại." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await db.SaveChangesAsync();
        return Ok(new { message = "Đổi mật khẩu thành công. Lần đăng nhập tiếp theo hãy sử dụng mật khẩu mới." });
    }

    /// <summary>Admin tìm kiếm, lọc và phân trang danh sách người dùng.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking().Include(x => x.Profile).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(x => x.Email.Contains(keyword) ||
                (x.Profile != null && x.Profile.FullName.Contains(keyword)));
        }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(x => x.Role == role);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new UserListItem(x.Id, x.Email, x.Role, x.Status, x.IsVerified,
                x.Profile == null ? null : x.Profile.FullName,
                x.Profile == null ? null : x.Profile.Phone, x.CreatedAt))
            .ToListAsync();

        return Ok(new PagedUsersResponse(items, page, pageSize, totalItems, totalPages));
    }

    /// <summary>Endpoint thử phân quyền: chỉ Admin vào được.</summary>
    [HttpGet("admin-ping")]
    [Authorize(Policy = Policies.AdminOnly)]
    public IActionResult AdminPing() => Ok("admin ok");

    [HttpPut("{id:guid}/lock")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IActionResult> Lock(Guid id) => SetStatus(id, UserStatus.Locked);

    [HttpPut("{id:guid}/unlock")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IActionResult> Unlock(Guid id) => SetStatus(id, UserStatus.Active);

    private async Task<IActionResult> SetStatus(Guid id, string status)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound(new { error = "Không tìm thấy tài khoản." });
        if (user.Role == Roles.Admin) return BadRequest(new { error = "Không thể khóa hoặc mở khóa tài khoản Admin qua chức năng này." });
        if (!user.IsVerified) return BadRequest(new { error = "Tài khoản chưa xác minh email." });
        if (user.Id == CurrentUserId()) return BadRequest(new { error = "Không thể tự khóa tài khoản đang đăng nhập." });

        if (user.Status == status) return Ok(new { user.Id, user.Status, message = "Trạng thái tài khoản không thay đổi." });
        if (status == UserStatus.Active && user.Status != UserStatus.Locked)
            return BadRequest(new { error = "Chỉ có thể mở khóa tài khoản đang bị khóa." });
        if (status == UserStatus.Locked && user.Status != UserStatus.Active)
            return BadRequest(new { error = "Chỉ có thể khóa tài khoản đang hoạt động." });

        user.Status = status;
        var adminId = CurrentUserId();
        if (status == UserStatus.Locked) db.AddOutbox(new UserLockedEvent(user.Id, adminId));
        else db.AddOutbox(new UserUnlockedEvent(user.Id, adminId));
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.Status, message = status == UserStatus.Locked ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản." });
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue("sub")!);
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
