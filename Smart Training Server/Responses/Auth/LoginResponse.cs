using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

// 닉네임 로그인 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Auth
{
    public class LoginResponse : BaseResponse
    {
        /// <summary>
        /// 로그인한 사용자 정보
        /// </summary>
        public UserInfo? User
        {
            get; set;
        }

        // 후속 HTTP 요청의 Authorization: Bearer 헤더에 넣을 인증 토큰 원문입니다.
        public string AccessToken { get; set; } = string.Empty;

        // 인증 헤더에 사용할 토큰 방식입니다. 현재는 Bearer입니다.
        public string TokenType { get; set; } = "Bearer";

        // 토큰 또는 입장 예약이 만료되는 UTC 시각입니다. 이 응답이 어느 토큰을 다루는지 함께 확인합니다.
        public DateTime? ExpiresAtUtc
        {
            get; set;
        }
    }
}
