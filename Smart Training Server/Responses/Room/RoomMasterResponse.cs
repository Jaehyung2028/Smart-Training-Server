using Smart_Training_Server.Responses.Base;

// 방장·방 설정 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Room
{
    public sealed class RoomMasterResponse : BaseResponse
    {
        // 현재 방장 사용자 ID입니다.
        public string MasterUserId { get; set; } = string.Empty;

        // 새 참가자의 입장이 잠겼는지 나타냅니다.
        public bool IsEntryLocked
        {
            get; set;
        }

        // 방의 표시 이름입니다.
        public string RoomName { get; set; } = string.Empty;

        // 방 최대 정원입니다. 실제 인원과 입장 예약을 포함한 조건은 서비스에서 검사합니다.
        public int MaxUsers
        {
            get; set;
        }

        // 방의 훈련 진행 여부입니다. 서버 프로세스가 실행 중인 상태와 구분합니다.
        public bool IsRunning
        {
            get; set;
        }

        // 방 진행 상태 버전입니다. 오래된 상태 변경 요청을 구별합니다.
        public long PhaseRevision
        {
            get; set;
        }
    }
}
