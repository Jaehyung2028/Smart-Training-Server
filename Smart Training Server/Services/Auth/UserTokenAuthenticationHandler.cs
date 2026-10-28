using Smart_Training_Server.Responses.Base;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

// Bearer 토큰을 검사하고 인증 사용자 정보를 ClaimsPrincipal에 담아 Controller에 전달합니다.
namespace Smart_Training_Server.Services.Auth
{
    // Authorization: Bearer <토큰>을 ASP.NET이 이해하는 인증 사용자(ClaimsPrincipal)로 변환합니다.
    public sealed class UserTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "UserToken";
        private readonly UserService users;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public UserTokenAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            UserService users
        ) : base(options, logger, encoder)
        {
            this.users = users;
        }


        // 인증 미들웨어가 호출합니다. 헤더가 없으면 NoResult, 잘못된 토큰이면 Fail, 유효하면 Success입니다.
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (header.Count != 1 || !UserAuthentication.TryReadBearer(header.ToString(), out var token))
            {
                return Task.FromResult(AuthenticateResult.Fail("Bearer 인증 토큰이 필요합니다."));
            }

            var user = users.FindByAccessToken(token);
            if (user == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("유효하지 않거나 만료된 인증 토큰입니다."));
            }

            // 확인한 사용자 ID와 닉네임을 Claim에 담습니다. Controller는 이 ID를 읽어 요청자를 판단합니다.
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.Name) },
                SchemeName
            );
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }


        // 인증이 필요한 요청에 유효한 인증이 없으면 401과 공통 오류 JSON을 반환합니다.
        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            await Response.WriteAsJsonAsync(ApiErrorResponse.Create(401, Context.TraceIdentifier));
        }
    }
}
