// 방장 위임·강퇴에서 공통으로 사용하는 대상 참가자 정보입니다. 동작과 권한 검사는 각 서비스 함수가 담당합니다.
namespace Smart_Training_Server.Requests.Room
{
    public sealed class RoomParticipantRequest
    {
        // 대상 방이 속한 로비입니다.
        public string LobbyId { get; set; } = string.Empty;

        // 대상 참가자가 접속한 방입니다.
        public string RoomId { get; set; } = string.Empty;

        // 요청자가 아니라 방장 위임·강퇴할 상대의 ID입니다. 요청자는 인증 토큰으로 확인합니다.
        public string TargetUserId { get; set; } = string.Empty;
    }
}
