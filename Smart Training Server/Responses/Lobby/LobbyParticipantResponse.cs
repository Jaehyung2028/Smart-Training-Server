namespace Smart_Training_Server.Responses.Lobby;

public sealed class LobbyParticipantResponse
{
    public string UserId { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string ConnectionState { get; init; } = "lobby";
    public string RoomId { get; init; } = "";
    public string RoomName { get; init; } = "";
    public bool IsRoomMaster { get; init; }
    public bool RoomIsRunning { get; init; }
}
