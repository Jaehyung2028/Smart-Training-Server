namespace Smart_Training_Server.Responses.Lobby;

public class ContentQueryResponse
{
    public string RootContent { get; init; } = string.Empty;
    public int Id { get; init; }
    public int ParentId { get; init; }
    public int Depth { get; init; }
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<ContentQueryResponse> Children { get; init; } = Array.Empty<ContentQueryResponse>();
}
