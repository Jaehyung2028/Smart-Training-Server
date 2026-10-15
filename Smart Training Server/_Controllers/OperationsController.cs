using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Services.Operations;

namespace Smart_Training_Server._Controllers;

[ApiController]
[AllowAnonymous]
public sealed class OperationsController(ServerOperationsService operations, LobbyShutdownService? shutdown = null) : ControllerBase
{
    private bool IsLocalRequest()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address == null || !IPAddress.IsLoopback(address)) return false;
        string host = Request.Host.Host.Trim('[', ']');
        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            (!IPAddress.TryParse(host, out var hostAddress) || !IPAddress.IsLoopback(hostAddress))) return false;
        // Do not trust forwarded headers or browser requests from another origin.
        if (Request.Headers.TryGetValue("Origin", out var origin) &&
            (origin.Count != 1 || !string.Equals(origin[0], Request.Scheme + "://" + Request.Host, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    [HttpGet("/api/operations")]
    public IActionResult GetStatus()
    {
        Response.Headers.CacheControl = "no-store";
        if (!IsLocalRequest()) return StatusCode(403, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 상태는 서버 PC의 localhost에서만 조회할 수 있습니다."));
        return Ok(operations.GetSnapshot());
    }

    [HttpGet("/operations")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
        if (!IsLocalRequest()) return StatusCode(403, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 화면은 서버 PC의 localhost에서 열어주세요."));
        return Content(OperationsPage.Html, "text/html; charset=utf-8");
    }

    [HttpPost("/api/operations/lobbies/{lobbyId}/mode")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public IActionResult SetOperatingMode(string lobbyId, Smart_Training_Server.Requests.Operations.SetOperatingModeRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        if (!IsLocalRequest() || Request.Headers["X-Operations-Request"] != "1")
            return StatusCode(403, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 설정은 서버 PC의 운영 페이지에서 변경해주세요."));
        if (request.ExpectedRevision == null) return BadRequest(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(400, HttpContext.TraceIdentifier, "현재 설정 버전이 필요합니다."));
        if (shutdown == null) return StatusCode(503);
        var result = shutdown.SetOperatingMode(lobbyId, request.Mode, request.AtUtc, request.Reason, request.ExpectedRevision.Value);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, result.Message));
    }

    // Compatibility endpoints also change the single operating mode, never independent flags.
    [HttpPost("/api/operations/lobbies/{lobbyId}/shutdown")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public IActionResult SetShutdown(string lobbyId, Smart_Training_Server.Requests.Operations.SetShutdownRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        if (!IsLocalRequest() || Request.Headers["X-Operations-Request"] != "1")
            return StatusCode(403, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "종료 설정은 서버 PC의 운영 페이지에서 변경해주세요."));
        if (request.ExpectedRevision == null) return BadRequest(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(400, HttpContext.TraceIdentifier, "현재 설정 버전이 필요합니다."));
        if (shutdown == null) return StatusCode(503);
        var result = shutdown.SetShutdown(lobbyId, request.AtUtc, request.Reason, request.ExpectedRevision.Value);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode,
            Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, result.Message));
    }

    [HttpPost("/api/operations/lobbies/{lobbyId}/maintenance")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public IActionResult SetMaintenance(string lobbyId, Smart_Training_Server.Requests.Operations.SetMaintenanceRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        if (!IsLocalRequest() || Request.Headers["X-Operations-Request"] != "1")
            return StatusCode(403, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "점검 설정은 서버 PC의 운영 페이지에서 변경해주세요."));
        if (request.Enabled == null || request.ExpectedRevision == null)
            return BadRequest(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(400, HttpContext.TraceIdentifier, "점검 여부와 현재 설정 버전이 필요합니다."));
        var result = operations.SetMaintenance(lobbyId, request.Enabled.Value, request.Reason, request.ExpectedRevision.Value);
        return result.Success ? Ok(result) : StatusCode(result.StatusCode,
            Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, result.Message));
    }
}
