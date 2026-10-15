using Smart_Training_Server.Responses.Lobby;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public (int StatusCode, IReadOnlyList<LobbyParticipantResponse> Participants) GetLobbyParticipants(string lobbyId, string authenticatedUserId)
    {
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            var lobby = lobbyService.FindById(lobbyId);
            if (lobby == null) return (404, []);
            var caller = userService.FindById(authenticatedUserId);
            if (caller == null || caller.CurrentLobbyId != lobbyId || !lobby.Users.ContainsKey(caller.Id)) return (403, []);
            var result = new List<LobbyParticipantResponse>();
            foreach (var member in lobby.Users.Values)
            {
                var user = userService.FindById(member.Id);
                if (user == null || user.CurrentLobbyId != lobbyId) continue;
                var room = user.CurrentRoomId.Length > 0 ? FindById(user.CurrentRoomId) : null;
                if (room != null && room.LobbyId == lobbyId && !room.IsClosing && room.Users.ContainsKey(user.Id) && connectedTokens.ContainsKey(user.Id))
                    result.Add(new() { UserId = user.Id, Nickname = user.Name, ConnectionState = "connected", RoomId = room.Id,
                        RoomName = room.Name, IsRoomMaster = room.MasterUserId == user.Id, RoomIsRunning = room.IsRunning });
                else if (reservations.TryGetValue(user.Id, out var reservation) && FindById(reservation.RoomId) is { } pending && pending.LobbyId == lobbyId && !pending.IsClosing)
                    result.Add(new() { UserId = user.Id, Nickname = user.Name, ConnectionState = "reserved", RoomId = pending.Id, RoomName = pending.Name });
                else result.Add(new() { UserId = user.Id, Nickname = user.Name });
            }
            return (200, result.OrderBy(item => item.Nickname, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.UserId, StringComparer.Ordinal).ToArray());
        }
    }
}
