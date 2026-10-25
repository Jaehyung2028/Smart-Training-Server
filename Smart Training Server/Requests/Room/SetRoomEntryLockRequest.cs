// 입장 잠금 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Room
{
    public sealed class SetRoomEntryLockRequest
    {
        // 대상 또는 소속 로비의 ID입니다.
        public string LobbyId { get; set; } = string.Empty;

        // 대상 또는 소속 방의 ID입니다.
        public string RoomId { get; set; } = string.Empty;

        // 새 참가자의 입장이 잠겼는지 나타냅니다.
        public bool? IsEntryLocked { get; set; }
    }
}
