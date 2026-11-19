using Microsoft.AspNetCore.Mvc;

namespace Contract.Api.Controllers;

[ApiController]
[Route("api/v1/contracts")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("contract ok");
}
