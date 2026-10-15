using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

namespace Smart_Training_Server.Services.Lobby;

public sealed partial class LobbyRoomUpdates(UserService users, RoomService rooms, LobbyService lobbies)
{
    private sealed class Subscription(string lobbyId, string token)
    {
        public string LobbyId { get; } = lobbyId;
        public string Token { get; } = token;
        public string? LastSnapshot { get; set; }
    }
    private readonly Dictionary<string, Subscription> subscriptions = new();

    public bool Subscribe(string connectionId, string lobbyId, string authorization)
    {
        lock (users.MembershipLock)
        {
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            var token = authorization[7..];
            var user = users.FindByAccessToken(token);
            if (user == null || lobbyId == null || (lobbyId.Length > 0 && user.CurrentLobbyId != lobbyId)) return false;
            roomChatSubscriptions.Remove(connectionId);
            subscriptions[connectionId] = new(lobbyId, token);
            return true;
        }
    }
    public void Remove(string connectionId) { lock (users.MembershipLock) { subscriptions.Remove(connectionId); roomChatSubscriptions.Remove(connectionId); } }

    // Only subscribed lobbies are inspected. Coalesce changes into one invalidation per tick.
    public IReadOnlyList<(string ConnectionId, string LobbyId, bool Ended)> CollectChanges()
    {
        lock (users.MembershipLock)
        {
            var result = new List<(string, string, bool)>();
            var snapshots = new Dictionary<string, string>();
            var lobbySnapshot = JsonSerializer.Serialize(lobbies.GetLobbySummaries());
            foreach (var (id, subscription) in subscriptions.ToArray())
            {
                var user = users.FindByAccessToken(subscription.Token);
                if (user == null || (subscription.LobbyId.Length > 0 && user.CurrentLobbyId != subscription.LobbyId))
                { subscriptions.Remove(id); roomChatSubscriptions.Remove(id); result.Add((id, subscription.LobbyId, true)); continue; }
                if (!snapshots.TryGetValue(subscription.LobbyId, out var snapshot))
                    snapshots[subscription.LobbyId] = snapshot = lobbySnapshot + "|" + (subscription.LobbyId.Length == 0 ? "" :
                        JsonSerializer.Serialize(rooms.GetRoomsByLobby(subscription.LobbyId)) + "|" + JsonSerializer.Serialize(rooms.GetLobbyParticipants(subscription.LobbyId, user.Id).Participants));
                if (snapshot == subscription.LastSnapshot) continue;
                subscription.LastSnapshot = snapshot;
                result.Add((id, subscription.LobbyId, false));
            }
            return result;
        }
    }
}

public sealed class LobbyRoomUpdatesService(LobbyRoomUpdates updates, IHubContext<LobbyUpdatesHub> hub,
    ILogger<LobbyRoomUpdatesService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (var change in updates.CollectChanges())
                {
                    try
                    {
                        await hub.Clients.Client(change.ConnectionId).SendAsync(
                            change.Ended ? "SubscriptionEnded" : "RoomsChanged", change.LobbyId, cancellationToken: stoppingToken);
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    { updates.Remove(change.ConnectionId); logger.LogWarning(error, "Lobby notification connection failed"); }
                }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
