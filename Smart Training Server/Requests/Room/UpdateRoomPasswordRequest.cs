// 비밀번호 변경 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Room
{
    public sealed class UpdateRoomPasswordRequest
    {
        // 대상 또는 소속 로비의 ID입니다.
        public string LobbyId { get; set; } = "";

        // 대상 또는 소속 방의 ID입니다.
        public string RoomId { get; set; } = "";

        // 빈 문자열은 비밀번호 해제이며, 누락이나 null은 올바른 요청이 아닙니다.
        public string? Password
        {
            get; set;
        }
    }
}
