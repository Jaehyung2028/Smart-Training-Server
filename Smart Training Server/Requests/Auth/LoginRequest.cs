// 닉네임 로그인 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Auth
{
    public class LoginRequest
    {
        /// <summary>
        /// 사용자가 입력한 닉네임
        /// </summary>
        public string UserName { get; set; } = string.Empty;
    }
}
