// HTTP API가 응답 가능한지 확인하는 상태 조회 결과입니다.
namespace Smart_Training_Server.Responses.Health
{
    public sealed class HealthResponse
    {
        public string Status { get; set; } = "ok";
    }
}
