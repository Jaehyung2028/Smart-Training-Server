namespace Smart_Training_Server.Responses.Lobby
{
    // 로그인 전에 공개하는 교육과정 목록입니다. 카탈로그 루트만 표시하며 참가자나 접속 비밀값은 포함하지 않습니다.
    public sealed record CourseSummaryResponse
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        // 점검·종료 예정 상태에서는 목록은 보여주되 입장은 제한합니다.
        public bool CanEnter { get; init; }

        public string UnavailableReason { get; init; } = string.Empty;
    }
}
