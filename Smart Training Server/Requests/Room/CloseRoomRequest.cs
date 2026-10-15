namespace Smart_Training_Server.Requests.Room;
public sealed class CloseRoomRequest
{
    public string LobbyId { get; set; } = "";
    public string RoomId { get; set; } = "";
}
