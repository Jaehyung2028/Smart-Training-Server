// 현재 사용자 상태 응답 객체입니다.
namespace Smart_Training_Server.Responses.Auth
{
    public sealed class CurrentUserResponse
    {
        // 이 객체의 고유 식별자입니다. 이름이 바뀌어도 ID를 기준으로 찾습니다.
        public string Id { get; init; } = string.Empty;

        // 화면에 표시하는 이름입니다.
        public string Name { get; init; } = string.Empty;

        // 입장이 완료된 현재 로비 ID입니다.
        public string CurrentLobbyId { get; init; } = string.Empty;

        // 현재 소속 로비의 표시 이름입니다.
        public string CurrentLobbyName { get; init; } = string.Empty;

        // 로비 자리 번호입니다. 미배정은 -1로 표현합니다.
        public int LobbyIndex { get; init; }

        // 접속이 확인된 현재 방 ID입니다.
        public string CurrentRoomId { get; init; } = string.Empty;

        // 현재 소속 방의 표시 이름입니다.
        public string CurrentRoomName { get; init; } = string.Empty;

        // 방 자리 번호입니다. 현재 소속과 함께 해석합니다.
        public int RoomIndex { get; init; }

        // 입장 예약 중인 방입니다. 실제 접속이 확정된 방과 구분합니다.
        public string PendingRoomId { get; init; } = string.Empty;
    }
}
