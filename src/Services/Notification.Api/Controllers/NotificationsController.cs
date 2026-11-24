
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

    // Lấy danh sách thông báo của người dùng
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var notifications = await db.Notifications
            .Where(n => n.UserId == CurrentUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(notifications);
    }

    // Đếm số thông báo chưa đọc
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var count = await db.Notifications
            .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

        return Ok(new { unreadCount = count });
    }

    // Đánh dấu một thông báo đã đọc
    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == CurrentUserId);

        if (notification is null)
            return NotFound();

        notification.IsRead = true;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // Đánh dấu tất cả thông báo đã đọc
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications
            .Where(n => n.UserId == CurrentUserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return NoContent();
    }
}
