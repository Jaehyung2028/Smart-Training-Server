namespace Smart_Training_Server.Requests.Room;
public sealed class UpdateRoomPasswordRequest
{
    public string LobbyId { get; set; } = "";
    public string RoomId { get; set; } = "";
    // Empty explicitly removes protection; omitted/null is invalid.
    public string? Password { get; set; }
}
