using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Smart_Training_Server._Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetStatus()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { status = "ok" });
    }
}
