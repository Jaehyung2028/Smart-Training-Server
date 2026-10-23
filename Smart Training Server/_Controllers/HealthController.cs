using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Responses.Health;

// API 프로세스에 HTTP 요청이 도달하는지 확인하는 상태 조회 API입니다. Unity UDP 접속 성공까지 보장하지는 않습니다.
namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/health")]
    public sealed class HealthController : ControllerBase
    {
        [HttpGet]
        [AllowAnonymous]
        // HTTP API가 응답 가능한지 확인할 상태를 반환합니다. Unity 전용 서버의 멀티 접속 검사와는 별개입니다.
        public ActionResult<HealthResponse> GetStatus()
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            return Ok(new HealthResponse());
        }
    }
}
