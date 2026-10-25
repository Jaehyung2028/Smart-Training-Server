using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Requests.Operations;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Services.Operations;

// 서버 PC의 localhost에서만 사용할 운영 API·페이지입니다. 익명 허용 대신 IP·Host·Origin과 변경 전용 헤더를 직접 검사합니다.
namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [AllowAnonymous]
    public sealed class OperationsController : ControllerBase
    {
        private readonly ServerOperationsService operations;

        private readonly LobbyOperatingModeService? operatingModes;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public OperationsController(ServerOperationsService operations, LobbyOperatingModeService? operatingModes = null)
        {
            this.operations = operations;
            this.operatingModes = operatingModes;
        }

        // 실제 원격 IP가 루프백이고 Host도 로컬인지 확인합니다. Origin이 있으면 같은 출처인지 검사하며 전달 헤더는 신뢰하지 않습니다.
        private bool IsLocalRequest()
        {
            var address = HttpContext.Connection.RemoteIpAddress;
            if (address?.IsIPv4MappedToIPv6 == true)
            {
                address = address.MapToIPv4();
            }

            if (address == null || !IPAddress.IsLoopback(address))
            {
                return false;
            }

            string host = Request.Host.Host.Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                (!IPAddress.TryParse(host, out var hostAddress) ||
                !IPAddress.IsLoopback(hostAddress)))
            {
                return false;
            }

            // 전달 헤더나 다른 출처의 브라우저 요청을 그대로 신뢰하지 않습니다.
            if (Request.Headers.TryGetValue("Origin", out var origin) &&
                (origin.Count != 1 ||
                !string.Equals(origin[0], Request.Scheme + "://" + Request.Host, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        [HttpGet("/api/operations")]
        // 서버 PC의 로컬 접근인지 확인한 뒤 운영 스냅샷을 반환합니다.
        public IActionResult GetStatus()
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            if (!IsLocalRequest())
            {
                return StatusCode(403, ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 상태는 서버 PC의 localhost에서만 조회할 수 있습니다."));
            }

            return Ok(operations.GetSnapshot());
        }

        [HttpGet("/operations")]
        [ApiExplorerSettings(IgnoreApi = true)]
        // 로컬 접근을 확인한 뒤 운영 HTML을 반환합니다. 캐시와 외부 프레임 삽입도 제한합니다.
        public IActionResult Page()
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            if (!IsLocalRequest())
            {
                return StatusCode(403, ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 화면은 서버 PC의 localhost에서 열어주세요."));
            }

            return Content(OperationsPage.Html, "text/html; charset=utf-8");
        }

        [HttpPost("/api/operations/lobbies/{lobbyId}/mode")]
        [Consumes("application/json")]
        [RequestSizeLimit(4096)]
        // 운영 중·점검·예약 종료 중 하나로 바꿉니다. 입력·현재 버전 검사는 담당 서비스와 저장소에서 수행합니다.
        public IActionResult SetOperatingMode(string lobbyId, SetOperatingModeRequest request)
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            if (!IsLocalRequest() || Request.Headers["X-Operations-Request"] != "1")
            {
                return StatusCode(403, ApiErrorResponse.Create(403, HttpContext.TraceIdentifier, "운영 설정은 서버 PC의 운영 페이지에서 변경해주세요."));
            }

            if (request.ExpectedRevision == null)
            {
                return BadRequest(ApiErrorResponse.Create(400, HttpContext.TraceIdentifier, "현재 설정 버전이 필요합니다."));
            }

            if (operatingModes == null)
            {
                return StatusCode(503);
            }

            var result = operatingModes.SetOperatingMode(lobbyId, request.Mode, request.AtUtc, request.Reason, request.ExpectedRevision.Value);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, result.Message));
        }
    }
}
