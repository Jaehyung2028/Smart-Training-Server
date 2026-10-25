using Smart_Training_Server.Enum;

// 방 목록 요약 응답 객체입니다.
// init 전용 조회 데이터이므로 record의 값 비교를 사용합니다. JSON 구조는 기존과 같습니다.
namespace Smart_Training_Server.Responses.Room
{
    // 조회 시점의 값만 복사합니다. 내부 사용자 객체와 접속 토큰을 포함하지 않습니다.
    public sealed record RoomSummaryResponse
    {
        // 이 객체의 고유 식별자입니다. 이름이 바뀌어도 ID를 기준으로 찾습니다.
        public string Id { get; init; } = string.Empty;

        // 대상 또는 소속 로비의 ID입니다.
        public string LobbyId { get; init; } = string.Empty;

        // 화면에 표시하는 이름입니다.
        public string Name { get; init; } = string.Empty;

        // 카탈로그의 최상위 콘텐츠 ID입니다.
        public string ContentId { get; init; } = string.Empty;

        // 같은 루트 안에서 실제로 선택한 하위 콘텐츠를 구분합니다.
        public int ContentNodeId { get; init; }

        // 방이 사용하는 콘텐츠의 표시 제목입니다.
        public string ContentTitle { get; init; } = string.Empty;

        // 방 최대 정원입니다. 실제 인원과 입장 예약을 포함한 조건은 서비스에서 검사합니다.
        public int MaxUsers { get; init; }

        // 실제 접속이 확인된 인원입니다.
        public int ConnectedUsers { get; init; }

        // 입장 예약은 했지만 아직 접속 확정 전인 인원입니다.
        public int ReservedUsers { get; init; }

        // 최대 정원에서 실제 접속 인원과 예약 인원을 뺀 남은 자리입니다.
        public int AvailableSlots { get; init; }

        // 현재 방장 사용자 ID입니다.
        public string MasterUserId { get; init; } = string.Empty;

        // 새 참가자의 입장이 잠겼는지 나타냅니다.
        public bool IsEntryLocked { get; init; }

        // 비밀번호가 설정된 방인지 나타내며 비밀번호 원문은 공개하지 않습니다.
        public bool HasPassword { get; init; }

        // 방의 훈련 진행 여부입니다. 서버 프로세스가 실행 중인 상태와 구분합니다.
        public bool IsRunning { get; init; }

        // 이 응답의 상태값입니다. 방 요약에서는 Unity 서버 프로세스의 상태를 의미합니다.
        public ServerState State { get; init; }

        // 서버에서 기록한 생성 UTC 시각입니다.
        public DateTime CreatedAt { get; init; }
    }
}
