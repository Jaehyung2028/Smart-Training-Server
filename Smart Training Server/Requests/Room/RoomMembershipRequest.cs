// 방 퇴장·예약 취소·방 닫기에 사용하는 대상 방 정보입니다. 각 작업의 권한은 서비스에서 따로 검사합니다.
namespace Smart_Training_Server.Requests.Room
{
    public sealed class RoomMembershipRequest
    {
        // 대상 방이 속한 로비입니다.
        public string LobbyId { get; set; } = string.Empty;

        // 작업할 방입니다. 요청자의 사용자 ID는 인증 토큰에서 별도로 확인합니다.
        public string RoomId { get; set; } = string.Empty;
    }
}
