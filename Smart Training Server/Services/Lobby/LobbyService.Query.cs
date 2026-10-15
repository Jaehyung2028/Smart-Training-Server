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

    private LobbySummaryResponse ToLobbySummary(LobbyInfo lobby) => new()
    {
        Id = lobby.Id,
        Name = lobby.Name,
        ContentId = lobby.IsCatalogLobby ? lobby.Id : string.Empty,
        ServerState = runtimes.TryGetValue(lobby.Id, out var runtime) ? runtime.State : "offline",
        PendingUsers = pendingJoins.Values.Count(x => x.LobbyId == lobby.Id),
        OperatingMode = operations.GetOperatingMode(lobby.Id).Mode, OperatingReason = operations.GetOperatingMode(lobby.Id).Reason, OperatingRevision = operations.GetOperatingMode(lobby.Id).Revision, ShutdownAtUtc = operations.GetShutdown(lobby.Id).AtUtc,
        ShutdownReason = operations.GetShutdown(lobby.Id).Reason,
        ShutdownState = operations.GetShutdown(lobby.Id).State,
        ShutdownRevision = operations.GetShutdown(lobby.Id).Revision,
        IsMaintenance = operations.GetMaintenance(lobby.Id).Enabled,
        MaintenanceReason = operations.GetMaintenance(lobby.Id).Reason,
        MaintenanceChangedAtUtc = operations.GetMaintenance(lobby.Id).ChangedAtUtc,
        MaintenanceRevision = operations.GetMaintenance(lobby.Id).Revision,
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
