using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Auth
{
    public class LoginResponse : BaseResponse
    {    
        /// <summary>
        /// 로그인한 사용자 정보
        /// </summary>
        public UserInfo? User { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
