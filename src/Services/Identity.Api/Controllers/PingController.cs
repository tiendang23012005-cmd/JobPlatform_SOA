using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("identity ok");
}
