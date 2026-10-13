namespace Smart_Training_Server.Services.Room;

internal sealed record RoomDisconnectNotice(string ConnectionId, string UserId, string AdmissionToken);

public partial class RoomService
{
    internal IReadOnlyList<RoomDisconnectNotice> RemoveUserFromRooms(string userId)
    {
        lock (userService.MembershipLock)
        {
            var notices = new List<RoomDisconnectNotice>();
            string? pendingRoomId = null;
            if (reservations.TryGetValue(userId, out var pending))
            {
                pendingRoomId = pending.RoomId;
                if (serverConnections.TryGetValue(pending.RoomId, out var pendingConnection))
                    notices.Add(new(pendingConnection, userId, pending.Token));
                RemoveReservation(pending);
            }
            foreach (var room in rooms.Values.ToArray())
            {
                bool wasMaster = room.MasterUserId == userId;
                bool removed = room.Users.TryRemove(userId, out _);
                bool ownsEmptyRoom = room.CreatorUserId == userId && room.Users.IsEmpty && room.ReservedUsers == 0;
                if (!removed && !wasMaster && !ownsEmptyRoom && room.Id != pendingRoomId) continue;
                if (removed && serverConnections.TryGetValue(room.Id, out var connection) &&
                    connectedTokens.TryGetValue(userId, out var token))
                    notices.Add(new(connection, userId, token));

                room.LastActiveTime = Now;
                var lobby = lobbyService.FindById(room.LobbyId);
                if (lobby != null) lobby.LastActiveTime = Now;
                if (room.Users.IsEmpty && room.ReservedUsers == 0)
                {
                    DeleteRoom(lobby, room);
                    if (lobby != null) lobbyService.RemoveEmptyLobby(lobby.Id);
                }
                else if (wasMaster)
                    ChangeMaster(room);
            }
            connectedTokens.Remove(userId);
            var user = userService.FindById(userId);
            if (user != null)
            {
                user.PendingRoomId = string.Empty;
                user.CurrentRoomId = string.Empty;
                user.CurrentRoomName = string.Empty;
                user.RoomIndex = -1;
            }
            return notices;
        }
    }
}
