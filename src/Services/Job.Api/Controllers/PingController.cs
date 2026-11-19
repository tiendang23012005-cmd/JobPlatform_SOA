using Microsoft.AspNetCore.Mvc;

namespace Job.Api.Controllers;

[ApiController]
[Route("api/v1/jobs")]
public class PingController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok("job ok");
}
