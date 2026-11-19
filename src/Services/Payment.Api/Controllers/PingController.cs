using Microsoft.AspNetCore.Mvc;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/v1/wallet")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("payment ok");
}
