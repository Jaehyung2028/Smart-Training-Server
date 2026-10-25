// 서버 내부의 방 입장 예약입니다. 실제 접속 전 자리를 확보하고 유효기간과 토큰으로 예약을 확인합니다.
namespace Smart_Training_Server.Models
{
    // 서버 내부 전용 예약 정보. 로비/방 목록 응답에 토큰을 노출하지 않습니다.
    internal sealed class RoomAdmission
    {
        // 이 자리를 예약한 사용자입니다.
        public string UserId { get; init; } = string.Empty;

        // 접속을 허용할 대상 방입니다.
        public string RoomId { get; init; } = string.Empty;

        // 클라이언트가 Unity 서버 연결 시 제시할 예약 확인용 난수 토큰입니다.
        public string Token { get; init; } = string.Empty;

        // 방 생성 때 확보한 자리를 최초 JoinRoom 요청이 이어받기 전인지 나타냅니다.
        // 한 번 이어받은 뒤의 중복 입장 요청은 기존과 같이 거절합니다.
        public bool AwaitingJoinRequest { get; set; }

        // 이 시각이 지나면 예약을 해제해 연결하지 않은 사용자가 자리를 계속 차지하지 못하게 합니다.
        public DateTime ExpiresAtUtc { get; set; }
    }
}
