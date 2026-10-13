using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Smart_Training_Server.Services.Auth;

public sealed class UserTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "UserToken";
    private readonly UserService users;

    public UserTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, UserService users) : base(options, logger, encoder)
    {
        this.users = users;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header.ToString(), out var parsed) ||
            !parsed.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || parsed.Parameter == null)
            return Task.FromResult(AuthenticateResult.Fail("Bearer 인증 토큰이 필요합니다."));
        var user = users.FindByAccessToken(parsed.Parameter);
        if (user == null) return Task.FromResult(AuthenticateResult.Fail("유효하지 않거나 만료된 인증 토큰입니다."));
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Name)
        }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(401, Context.TraceIdentifier));
    }
}
