using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Responses.Auth;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Services.Auth;

// 로그인·내 정보·로그아웃·토큰 갱신 HTTP API입니다. 인증이 필요한 요청과 익명 허용 요청이 함수별로 다릅니다.
namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // HTTP 요청과 응답을 담당하는 입구입니다. 실제 사용자·세션 규칙은 UserService에서 처리합니다.
    public class AuthController : ControllerBase
    {
        private readonly UserService userService;
        private readonly UserLifecycleService lifecycleService;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public AuthController(UserService userService, UserLifecycleService lifecycleService)
        {
            this.userService = userService;
            this.lifecycleService = lifecycleService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        // 로그인 입력을 UserService에 전달하고 성공이면 200, 업무 실패면 400으로 응답합니다.
        public ActionResult<LoginResponse> Login(LoginRequest request)
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            // Pragma는 과거 HTTP/1.0 방식의 캐시 제어와 호환하기 위한 보조 헤더입니다.
            // 응답의 Pragma: no-cache는 모든 캐시에서 보장되지 않으므로, 위 Cache-Control: no-store가 주 설정입니다.
            Response.Headers.Pragma = "no-cache";
            LoginResponse response = userService.Login(request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpGet("me")]
        [Authorize]
        // 인증 Claim에서 사용자 ID를 얻어 현재 상태를 조회합니다. 사용자가 사라졌으면 401로 재인증을 요구합니다.
        public ActionResult<CurrentUserResponse> Me()
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            // Pragma는 과거 HTTP/1.0 방식의 캐시 제어와 호환하기 위한 보조 헤더입니다.
            // 응답의 Pragma: no-cache는 모든 캐시에서 보장되지 않으므로, 위 Cache-Control: no-store가 주 설정입니다.
            Response.Headers.Pragma = "no-cache";
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
            var user = userService.GetCurrentUser(userId);
            if (user == null)
            {
                Response.Headers.WWWAuthenticate = "Bearer";
                return Unauthorized(ApiErrorResponse.Create(401, HttpContext.TraceIdentifier));
            }

            return Ok(user);
        }

        [HttpPost("logout")]
        [Authorize]
        // 인증된 요청자의 로그아웃을 소속·세션 정리 서비스에 전달합니다.
        public async Task<ActionResult<BaseResponse>> Logout()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
            return Ok(await lifecycleService.LogoutAsync(userId));
        }

        // 만료 직후에는 일반 인증이 실패하므로 이 함수에서 유예 기한을 직접 검사합니다.
        [HttpPost("refresh")]
        [AllowAnonymous]
        public ActionResult<LoginResponse> Refresh()
        {
            // Response는 클라이언트에 보낼 HTTP 응답이고, Headers는 본문 외에 전달하는 처리 지침입니다.
            // Cache-Control: no-store를 보내 브라우저·중간 캐시가 이 응답을 저장하지 않도록 요청합니다.
            // 인증 정보나 현재 상태가 캐시에 남거나 저장된 예전 응답으로 재사용되는 것을 막기 위한 설정입니다.
            Response.Headers.CacheControl = "no-store";
            // Pragma는 과거 HTTP/1.0 방식의 캐시 제어와 호환하기 위한 보조 헤더입니다.
            // 응답의 Pragma: no-cache는 모든 캐시에서 보장되지 않으므로, 위 Cache-Control: no-store가 주 설정입니다.
            Response.Headers.Pragma = "no-cache";
            if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var header
            ) ||
                !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
                header.Parameter == null)
            {
                return RefreshUnauthorized();
            }

            var response = userService.RefreshSession(header.Parameter);
            if (!response.Success)
            {
                return RefreshUnauthorized();
            }

            return Ok(response);
        }

        // 갱신 실패를 401과 Bearer 재인증 헤더로 응답합니다.
        private UnauthorizedObjectResult RefreshUnauthorized()
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized(ApiErrorResponse.Create(401, HttpContext.TraceIdentifier, "갱신할 수 없는 인증 토큰입니다. 다시 로그인해주세요."));
        }
    }
}
