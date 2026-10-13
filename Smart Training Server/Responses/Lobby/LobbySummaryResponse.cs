namespace Smart_Training_Server.Responses.Lobby;

public class LobbySummaryResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int UserCount { get; init; }
    public int RoomCount { get; init; }
    public int ContentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
