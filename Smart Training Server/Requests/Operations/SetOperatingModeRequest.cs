using System.ComponentModel.DataAnnotations;

// 통합 운영 모드 변경 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Operations
{
    public sealed class SetOperatingModeRequest
    {
        // 운영 모드 입력입니다. Required로 누락을 막고, 허용 문자열은 서비스에서 검사합니다.
        [Required]
        public string? Mode { get; set; }

        // 운영 변경 사유입니다. 필수이며 StringLength로 1~200자 범위를 검사합니다.
        [Required, StringLength(200, MinimumLength = 1)]
        public string? Reason { get; set; }

        // 종료 예약 모드에서는 필수인 종료 시각입니다. 운영 재개·점검 모드에서는 null이어야 합니다.
        public DateTimeOffset? AtUtc { get; set; }

        // 화면이 조회한 현재 버전입니다. 필수·0 이상이며, 서버 최신 버전과 달라지면 변경을 거절합니다.
        [Required, Range(0, long.MaxValue)]
        public long? ExpectedRevision { get; set; }
    }
}
