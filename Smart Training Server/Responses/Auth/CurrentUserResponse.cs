namespace Smart_Training_Server.Responses.Auth;

public sealed class CurrentUserResponse
{
    public string PendingLobbyId { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string CurrentLobbyId { get; init; } = string.Empty;
    public string CurrentLobbyName { get; init; } = string.Empty;
    public int LobbyIndex { get; init; }
    public string CurrentRoomId { get; init; } = string.Empty;
    public string CurrentRoomName { get; init; } = string.Empty;
    public int RoomIndex { get; init; }
    public string PendingRoomId { get; init; } = string.Empty;
}
