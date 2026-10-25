// 로비 목록 요약 응답 객체입니다.
// init 전용 조회 데이터이므로 record의 값 비교를 사용합니다. JSON 구조는 기존과 같습니다.
namespace Smart_Training_Server.Responses.Lobby
{
    public record LobbySummaryResponse
    {
        // 이 객체의 고유 식별자입니다. 이름이 바뀌어도 ID를 기준으로 찾습니다.
        public string Id { get; init; } = string.Empty;

        // 화면에 표시하는 이름입니다.
        public string Name { get; init; } = string.Empty;

        // 카탈로그의 최상위 콘텐츠 ID입니다.
        public string ContentId { get; init; } = string.Empty;

        // 예약된 종료 UTC 시각입니다.
        public DateTime? ShutdownAtUtc { get; init; }

        // 종료 예약 사유입니다.
        public string ShutdownReason { get; init; } = "";

        // 종료 예약의 없음·예정·완료 상태입니다.
        public string ShutdownState { get; init; } = "none";

        // 종료 조회 형식으로 전달하는 운영 설정 버전입니다.
        public long ShutdownRevision { get; init; }

        // running은 입장 가능, maintenance는 점검, scheduled는 종료 예정 상태입니다.
        public string OperatingMode { get; init; } = "running";

        // 현재 운영 모드로 변경한 이유입니다.
        public string OperatingReason { get; init; } = "";

        // 운영 설정의 현재 버전입니다. 오래된 설정 요청과 구분합니다.
        public long OperatingRevision { get; init; }

        // 통합 운영 설정에서 점검 상태인지 표시합니다. 별도의 독립된 운영 모드는 아닙니다.
        public bool IsMaintenance { get; init; }

        // 점검 사유입니다.
        public string MaintenanceReason { get; init; } = string.Empty;

        // 점검 상태 변경 시각입니다.
        public DateTime? MaintenanceChangedAtUtc { get; init; }

        // 점검 조회 형식으로 전달하는 운영 설정 버전입니다.
        public long MaintenanceRevision { get; init; }

        // 로비 소속 사용자 수입니다.
        public int UserCount { get; init; }

        // 현재 로비에 등록된 방 개수입니다.
        public int RoomCount { get; init; }

        // 로비에 연결된 최상위 콘텐츠 개수입니다.
        public int ContentCount { get; init; }

        // 서버에서 기록한 생성 UTC 시각입니다.
        public DateTime CreatedAt { get; init; }
    }
}
