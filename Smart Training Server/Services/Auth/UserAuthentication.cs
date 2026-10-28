using System.Net.Http.Headers;
using System.Security.Claims;

namespace Smart_Training_Server.Services.Auth
{
    // HTTP 인증 구문과 인증 결과에서 사용자 ID를 읽는 공통 규칙입니다. 세션 저장소나 Controller에 의존하지 않습니다.
    public static class UserAuthentication
    {
        public static bool TryReadBearer(string? authorization, out string token)
        {
            token = string.Empty;
            if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
                !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(header.Parameter))
            {
                return false;
            }

            token = header.Parameter;
            return true;
        }


        public static string GetRequiredUserId(this ClaimsPrincipal user)
        {
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
        }
    }
}
