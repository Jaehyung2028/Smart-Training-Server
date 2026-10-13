using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public RoomMasterResponse SetRoomEntryLock(string authenticatedUserId, SetRoomEntryLockRequest request)
    {
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrWhiteSpace(request.LobbyId) || string.IsNullOrWhiteSpace(request.RoomId) || request.IsEntryLocked == null)
                return new() { Success = false, Message = "로비, 방, 입장 잠금 상태를 지정해주세요." };
            var room = FindById(request.RoomId);
            if (room == null || room.LobbyId != request.LobbyId)
                return new() { Success = false, Message = "해당 로비에서 방을 찾을 수 없습니다." };
            var caller = userService.FindById(authenticatedUserId);
            if (caller == null || caller.CurrentRoomId != room.Id || !room.Users.ContainsKey(caller.Id) ||
                !connectedTokens.ContainsKey(caller.Id) || room.MasterUserId != caller.Id)
                return new() { Success = false, Message = "현재 방장만 입장 잠금을 변경할 수 있습니다." };
            room.IsEntryLocked = request.IsEntryLocked.Value;
            if (room.IsEntryLocked)
                foreach (var admission in reservations.Values.Where(value => value.RoomId == room.Id).ToArray())
                    RemoveReservation(admission);
            room.LastActiveTime = Now;
            return new() { Success = true, MasterUserId = room.MasterUserId, IsEntryLocked = room.IsEntryLocked,
                Message = room.IsEntryLocked ? "새 참가자의 입장을 잠갔습니다." : "새 참가자의 입장을 열었습니다." };
        }
    }
}
