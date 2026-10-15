using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Room;
public partial class RoomService
{
    public async Task<BaseResponse> CloseRoomAsync(string userId, CloseRoomRequest request, IHubContext<RoomHub> hub)
    {
        Models.RoomInfo room;
        string? connection;
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrWhiteSpace(request.RoomId) || string.IsNullOrWhiteSpace(request.LobbyId))
                return Result(false, "로비와 방을 지정해주세요.");
            var found = FindById(request.RoomId);
            var caller = string.IsNullOrWhiteSpace(userId) ? null : userService.FindById(userId);
            if (found == null || found.LobbyId != request.LobbyId) return Result(false, "방을 찾을 수 없습니다.");
            if (found.IsClosing || caller == null || caller.CurrentRoomId != found.Id || found.MasterUserId != userId ||
                !found.Users.ContainsKey(userId) || !connectedTokens.ContainsKey(userId))
                return Result(false, "현재 접속한 방장만 방을 닫을 수 있습니다.");
            room = found; room.IsClosing = true; room.IsEntryLocked = true; room.State = ServerState.Stopping;
            connection = serverConnections.GetValueOrDefault(room.Id);
            foreach (var reservation in reservations.Values.Where(x => x.RoomId == room.Id).ToArray()) RemoveReservation(reservation);
            foreach (var user in room.Users.Values)
            {
                user.CurrentRoomId = ""; user.CurrentRoomName = ""; user.RoomIndex = -1;
                connectedTokens.Remove(user.Id);
            }
            room.Users.Clear();
        }
        try
        {
            if (connection != null)
            {
                await hub.Clients.Client(connection).SendAsync("CloseRoom", room.Id).WaitAsync(TimeSpan.FromSeconds(3));
                // Give the dedicated process time to deliver reliable disconnection reasons.
                await Task.Delay(750);
            }
        }
        catch (Exception) { /* Cleanup remains mandatory if the dedicated process is unavailable. */ }
        finally
        {
            lock (userService.MembershipLock)
            {
                ResetRoomServer(room, "방장이 방을 닫았습니다.");
                DeleteRoom(lobbyService.FindById(room.LobbyId), room);
            }
        }
        return Result(true, "방을 닫았습니다. 로비로 돌아왔습니다.");
    }
}
