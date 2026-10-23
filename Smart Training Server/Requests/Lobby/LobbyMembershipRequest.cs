// 로비 입장·퇴장에서 공통으로 사용하는 대상 로비 정보입니다. 작업은 API 주소와 함수명으로 구분합니다.
namespace Smart_Training_Server.Requests.Lobby
{
    public sealed class LobbyMembershipRequest
    {
        // 요청자 ID는 본문으로 받지 않습니다. Controller가 인증 토큰에서 확인해 서비스에 전달합니다.
        public string LobbyId { get; set; } = string.Empty;
    }
}
