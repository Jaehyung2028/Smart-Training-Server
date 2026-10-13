namespace Smart_Training_Server.Requests.Room;

public sealed class TransferRoomMasterRequest
{
    public string LobbyId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
}
