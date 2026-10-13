namespace Smart_Training_Server.Models;

// 서버 내부 전용 예약 정보. 로비/방 목록 응답에 토큰을 노출하지 않습니다.
internal sealed class RoomAdmission
{
    public string UserId { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
