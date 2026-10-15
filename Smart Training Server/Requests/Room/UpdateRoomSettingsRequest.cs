namespace Smart_Training_Server.Requests.Room;

public sealed class UpdateRoomSettingsRequest
{
    public string LobbyId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public int? MaxUsers { get; set; }
}
