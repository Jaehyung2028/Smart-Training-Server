namespace Smart_Training_Server.Services.Lobby;
public partial class LobbyService
{
    // Caller holds MembershipLock. Release pending requests before stopping their readiness task.
    internal void EvacuateLobby(string id)
    {
        var lobby = FindById(id);
        if (lobby?.IsCatalogLobby != true) return;
        foreach (var pending in pendingJoins.Values.Where(x => x.LobbyId == id).ToArray()) ReleasePendingJoin(pending);
        foreach (var user in lobby.Users.Values)
        { user.CurrentLobbyId = ""; user.CurrentLobbyName = ""; user.LobbyIndex = -1; }
        lobby.Users.Clear();
        StopIdleLobby(lobby);
    }
}
