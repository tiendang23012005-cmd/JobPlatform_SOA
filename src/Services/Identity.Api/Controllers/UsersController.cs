
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
public class UsersController(
    IdentityDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("admin-ping")]
    [Authorize(Policy = Policies.AdminOnly)]
    public IActionResult AdminPing() => Ok("admin ok");

    [HttpPut("{id:guid}/lock")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IActionResult> Lock(Guid id)
        => SetStatus(id, UserStatus.Locked);

    [HttpPut("{id:guid}/unlock")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<IActionResult> Unlock(Guid id)
        => SetStatus(id, UserStatus.Active);

    private async Task<IActionResult> SetStatus(Guid id, string status)
    {
        var user = await db.Users.FindAsync(id);

        if (user is null)
            return NotFound();

        if (user.Role == Roles.Admin)
            return BadRequest(new
            {
                error = "Không thể khóa tài khoản Admin."
            });

        if (!user.IsVerified)
            return BadRequest(new
            {
                error = "Tài khoản chưa xác minh email."
            });

        user.Status = status;
        await db.SaveChangesAsync();

        return Ok(new { user.Id, user.Status });
    }

    [HttpGet("internal/{id:guid}/email")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUserEmail(Guid id)
    {
        var expectedKey = configuration["InternalApi:Key"];
        var providedKey = Request.Headers["X-Internal-Api-Key"].ToString();

        if (string.IsNullOrWhiteSpace(expectedKey) ||
            string.IsNullOrWhiteSpace(providedKey))
            return Unauthorized();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(
                expectedBytes, providedBytes))
            return Unauthorized();

        var user = await db.Users
            .Where(u => u.Id == id)
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync();

        if (user is null)
            return NotFound();

        return Ok(user);
    }
}
