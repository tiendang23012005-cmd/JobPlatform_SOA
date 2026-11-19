using Microsoft.AspNetCore.Mvc;

namespace Notification.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("notification ok");
}
