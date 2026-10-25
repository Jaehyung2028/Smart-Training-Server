// appsettings의 Auth 설정을 받습니다. 닉네임 규칙과 인증 토큰·갱신 유예 기간을 결정합니다.
namespace Smart_Training_Server.Options
{
    public class AuthOptions
    {
        // 인증 토큰의 유효기간(분)입니다.
        public int AccessTokenLifetimeMinutes { get; set; } = 60;

        // 토큰 만료 후 세션을 갱신할 수 있는 유예기간(분)입니다.
        public int SessionGraceMinutes { get; set; } = 5;

        // 허용하는 이름 길이의 최솟값입니다.
        public int NameMinLength { get; set; }

        // 허용하는 이름 길이의 최댓값입니다.
        public int NameMaxLength { get; set; }

        // 같은 이름을 허용할지 결정하는 설정입니다.
        public bool AllowDuplicateName { get; set; }
    }
}
