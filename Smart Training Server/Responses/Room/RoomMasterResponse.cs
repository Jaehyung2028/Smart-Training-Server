using Smart_Training_Server.Responses.Base;

// 방장·방 설정 응답 객체입니다.
namespace Smart_Training_Server.Responses.Room
{
    public sealed class RoomMasterResponse : BaseResponse
    {
        // 전용 서버가 해당 콘텐츠에 필요한 네트워크 오브젝트를 준비할 때 사용합니다.
        public string ContentType { get; set; } = string.Empty;

        // 현재 방장 사용자 ID입니다.
        public string MasterUserId { get; set; } = string.Empty;

        // 새 참가자의 입장이 잠겼는지 나타냅니다.
        public bool IsEntryLocked { get; set; }

        // 방의 표시 이름입니다.
        public string RoomName { get; set; } = string.Empty;

        // 방 최대 정원입니다. 실제 인원과 입장 예약을 포함한 조건은 서비스에서 검사합니다.
        public int MaxUsers { get; set; }

        // 방의 훈련 진행 여부입니다. 서버 프로세스가 실행 중인 상태와 구분합니다.
        public bool IsRunning { get; set; }

        // 방 진행 상태 버전입니다. 오래된 상태 변경 요청을 구별합니다.
        public long PhaseRevision { get; set; }
    }
}
