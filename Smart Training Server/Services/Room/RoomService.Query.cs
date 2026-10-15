using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Room;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    // null: 없는 로비, 빈 배열: 존재하지만 방이 없는 로비.
    public IReadOnlyList<RoomSummaryResponse>? GetRoomsByLobby(string lobbyId)
    {
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            var lobby = lobbyService.FindById(lobbyId);
            return lobby?.Rooms.Values.OrderBy(room => room.CreatedAt)
                .ThenBy(room => room.Id, StringComparer.Ordinal)
                .Select(ToRoomSummary).ToArray();
        }
    }

    public RoomSummaryResponse? GetRoomByLobby(string lobbyId, string roomId)
    {
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            var lobby = lobbyService.FindById(lobbyId);
            var room = lobby == null ? null : FindById(lobby, roomId);
            return room == null ? null : ToRoomSummary(room);
        }
    }

    private static RoomSummaryResponse ToRoomSummary(RoomInfo room) => new()
    {
        Id = room.Id,
        LobbyId = room.LobbyId,
        Name = room.Name,
        ContentId = room.Content.RootContent,
        ContentTitle = room.Content.Title,
        MaxUsers = room.MaxUsers,
        ConnectedUsers = room.Users.Count,
        ReservedUsers = room.ReservedUsers,
        AvailableSlots = Math.Max(0, room.MaxUsers - room.Users.Count - room.ReservedUsers),
        MasterUserId = room.MasterUserId,
        IsEntryLocked = room.IsEntryLocked,
        HasPassword = room.HasPassword,
        IsRunning = room.IsRunning,
        State = room.State,
        CreatedAt = room.CreatedAt
    };
}
