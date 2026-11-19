using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notification.Api.Data;

namespace Notification.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController(NotificationDbContext db) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Notifications.Where(n => n.UserId == CurrentUserId)
            .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync());

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == CurrentUserId);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
