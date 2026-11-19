using System.Security.Claims;
using Identity.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Constants;
using Shared.Infrastructure.Extensions;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController(IdentityDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = Guid.Parse(User.FindFirstValue("sub")!);
        var u = await db.Users.Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound();
        return Ok(new { u.Id, u.Email, u.Role, u.Status, u.Profile?.FullName, u.Profile?.Phone, u.Profile?.Skills, u.Profile?.Bio });
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
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        if (u.Role == Roles.Admin) return BadRequest(new { error = "Không thể khóa tài khoản Admin." });
        if (!u.IsVerified) return BadRequest(new { error = "Tài khoản chưa xác minh email." });
        u.Status = status;
        await db.SaveChangesAsync();
        return Ok(new { u.Id, u.Status });
    }
}
