namespace Smart_Training_Server.Requests.Room;

public sealed class SetRoomEntryLockRequest
{
    public string LobbyId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public bool? IsEntryLocked { get; set; }
}
