// 한 임시 사용자의 식별 정보와 현재 로비·방, 입장 대기 상태를 보관합니다.
namespace Smart_Training_Server.Models
{
    // 로그인한 임시 사용자의 현재 소속과 입장 대기 상태를 저장합니다. DB 계정 정보와는 다릅니다.
    public class UserInfo
    {
        // 자리를 예약했지만 실제 멀티 접속이 확정되지 않은 방입니다.
        public string PendingRoomId { get; set; } = string.Empty;

        // 로비에서 사용하는 사용자 자리 번호입니다. -1은 미배정 상태입니다.
        public int LobbyIndex { get; set; } = -1;

        // 방 접속 확정 시 배정하는 자리 번호입니다. 퇴장하면 -1로 되돌립니다.
        public int RoomIndex { get; set; } = -1;

        // 사용자를 구분하는 GUID입니다. 닉네임 대신 이 값을 기준으로 소속을 관리합니다.
        public string Id { get; set; } = string.Empty;

        // 화면에 표시하는 닉네임입니다.
        public string Name { get; set; } = string.Empty;

        // 입장이 완료된 현재 로비 ID입니다.
        public string CurrentLobbyId { get; set; } = string.Empty;

        // 화면 표시를 위해 보관하는 현재 로비 이름입니다.
        public string CurrentLobbyName { get; set; } = string.Empty;

        // 실제 방 접속이 확인된 후 설정합니다. 예약만으로는 이 값을 채우지 않습니다.
        public string CurrentRoomId { get; set; } = string.Empty;

        // 현재 방 이름입니다. 방 이름 변경 시 참가자의 이 값도 함께 갱신합니다.
        public string CurrentRoomName { get; set; } = string.Empty;
    }
}
