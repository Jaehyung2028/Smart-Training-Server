using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Lobby;

namespace Smart_Training_Server.Services.Lobby;

public partial class LobbyService
{
    public IReadOnlyList<LobbySummaryResponse> GetLobbySummaries()
    {
        lock (userService.MembershipLock)
            return lobbies.Values.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(ToLobbySummary).ToArray();
    }

    public LobbySummaryResponse? GetLobbySummary(string lobbyId)
    {
        lock (userService.MembershipLock)
            return lobbies.TryGetValue(lobbyId, out var lobby) ? ToLobbySummary(lobby) : null;
    }

    public IReadOnlyList<ContentQueryResponse>? GetContentsByLobby(string lobbyId)
    {
        lock (userService.MembershipLock)
            return lobbies.TryGetValue(lobbyId, out var lobby)
                ? lobby.Contents.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => ToContentQuery(x.Value)).ToArray()
                : null;
    }

    private static LobbySummaryResponse ToLobbySummary(LobbyInfo lobby) => new()
    {
        Id = lobby.Id,
        Name = lobby.Name,
        UserCount = lobby.Users.Count,
        RoomCount = lobby.Rooms.Count,
        ContentCount = lobby.Contents.Count,
        CreatedAt = lobby.CreatedAt
    };

    private static ContentQueryResponse ToContentQuery(ContentInfo content) => new()
    {
        RootContent = content.RootContent,
        Id = content.Id,
        ParentId = content.ParentId,
        Depth = content.Depth,
        Title = content.Title,
        Children = content.Children.Select(ToContentQuery).ToArray()
    };
}
