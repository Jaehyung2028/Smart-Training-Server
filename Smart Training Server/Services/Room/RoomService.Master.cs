using Smart_Training_Server.Responses.Room;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public RoomMasterResponse TransferRoomMaster(string authenticatedUserId,
        Smart_Training_Server.Requests.Room.TransferRoomMasterRequest request)
    {
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrWhiteSpace(request.LobbyId) || string.IsNullOrWhiteSpace(request.RoomId) ||
                string.IsNullOrWhiteSpace(request.TargetUserId))
                return new() { Success = false, Message = "로비, 방, 위임할 참가자를 지정해주세요." };
            var room = FindById(request.RoomId);
            if (room == null || room.LobbyId != request.LobbyId)
                return new() { Success = false, Message = "해당 로비에서 방을 찾을 수 없습니다." };
            var caller = userService.FindById(authenticatedUserId);
            if (caller == null || caller.CurrentRoomId != room.Id || !room.Users.ContainsKey(caller.Id) ||
                !connectedTokens.ContainsKey(caller.Id) || room.MasterUserId != caller.Id)
                return new() { Success = false, Message = "현재 방장만 방장을 위임할 수 있습니다." };
            var target = userService.FindById(request.TargetUserId);
            if (target == null || target.CurrentRoomId != room.Id || !room.Users.ContainsKey(target.Id) ||
                !connectedTokens.ContainsKey(target.Id))
                return new() { Success = false, Message = "같은 방에 접속 중인 참가자에게만 위임할 수 있습니다." };
            if (target.Id == caller.Id)
                return new() { Success = true, MasterUserId = caller.Id, IsEntryLocked = room.IsEntryLocked, Message = "이미 방장입니다." };
            room.MasterUserId = target.Id;
            room.LastActiveTime = Now;
            return new() { Success = true, MasterUserId = target.Id, IsEntryLocked = room.IsEntryLocked, Message = "방장을 위임했습니다." };
        }
    }

    // Only the active registered dedicated server can retrieve this room's authority.
    public RoomMasterResponse GetRoomMaster(string roomId, string connectionId)
    {
        lock (userService.MembershipLock)
        {
            if (!IsRegisteredServer(roomId, connectionId))
                return new() { Success = false, Message = "등록된 방 서버만 조회할 수 있습니다." };
            var room = FindById(roomId);
            if (room == null)
                return new() { Success = false, Message = "방을 찾을 수 없습니다." };
            return new() { Success = true, MasterUserId = room.MasterUserId, IsEntryLocked = room.IsEntryLocked,
                RoomName = room.Name, MaxUsers = room.MaxUsers, IsRunning = room.IsRunning, PhaseRevision = room.PhaseRevision };
        }
    }
}
