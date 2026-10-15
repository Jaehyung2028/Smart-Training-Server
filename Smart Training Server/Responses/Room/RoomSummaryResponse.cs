using Smart_Training_Server.Enum;

namespace Smart_Training_Server.Responses.Room;

// 조회 시점의 값만 복사합니다. 내부 사용자 객체와 접속 토큰을 포함하지 않습니다.
public sealed class RoomSummaryResponse
{
    public string Id { get; init; } = string.Empty;
    public string LobbyId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ContentId { get; init; } = string.Empty;
    public string ContentTitle { get; init; } = string.Empty;
    public int MaxUsers { get; init; }
    public int ConnectedUsers { get; init; }
    public int ReservedUsers { get; init; }
    public int AvailableSlots { get; init; }
    public string MasterUserId { get; init; } = string.Empty;
    public bool IsEntryLocked { get; init; }
    public bool HasPassword { get; init; }
    public bool IsRunning { get; init; }
    public ServerState State { get; init; }
    public DateTime CreatedAt { get; init; }
}
