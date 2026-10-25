// 방 설정 변경 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Room
{
    public sealed class UpdateRoomSettingsRequest
    {
        // 대상 또는 소속 로비의 ID입니다.
        public string LobbyId { get; set; } = string.Empty;

        // 대상 또는 소속 방의 ID입니다.
        public string RoomId { get; set; } = string.Empty;

        // 방의 표시 이름입니다.
        public string RoomName { get; set; } = string.Empty;

        // 방 최대 정원입니다. 실제 인원과 입장 예약을 포함한 조건은 서비스에서 검사합니다.
        public int? MaxUsers { get; set; }
    }
}
