using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Responses.Auth;
using Smart_Training_Server.Services.Auth;

namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserService userService;
        private readonly UserLifecycleService lifecycleService;

        public AuthController(UserService userService, UserLifecycleService lifecycleService)
        {
            this.userService = userService;
            this.lifecycleService = lifecycleService;
        }

        [HttpPost("login")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public ActionResult<LoginResponse> Login(LoginRequest request)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            LoginResponse response = userService.Login(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpGet("me")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public ActionResult<CurrentUserResponse> Me()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
            var user = userService.GetCurrentUser(userId);
            if (user == null)
            {
                Response.Headers.WWWAuthenticate = "Bearer";
                return Unauthorized(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(401, HttpContext.TraceIdentifier));
            }
            return Ok(user);
        }

        [HttpPost("logout")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<Smart_Training_Server.Responses.Base.BaseResponse>> Logout()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
            return Ok(await lifecycleService.LogoutAsync(userId));
        }

        // 만료 직후에는 일반 인증이 실패하므로 이 함수에서 유예 기한을 직접 검사합니다.
        [HttpPost("refresh")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public ActionResult<LoginResponse> Refresh()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header) ||
                !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter == null)
                return RefreshUnauthorized();
            var response = userService.RefreshSession(header.Parameter);
            if (!response.Success) return RefreshUnauthorized();
            return Ok(response);
        }

        private UnauthorizedObjectResult RefreshUnauthorized()
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(401, HttpContext.TraceIdentifier, "갱신할 수 없는 인증 토큰입니다. 다시 로그인해주세요."));
        }
    }
}
