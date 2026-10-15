using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public RoomMasterResponse UpdateRoomSettings(string authenticatedUserId, UpdateRoomSettingsRequest request)
    {
        lock (userService.MembershipLock)
        {
            string name = request.RoomName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(request.LobbyId) || string.IsNullOrWhiteSpace(request.RoomId) ||
                name.Length < roomOptions.NameMinLength || name.Length > roomOptions.NameMaxLength || string.IsNullOrWhiteSpace(name) ||
                request.MaxUsers == null || request.MaxUsers < roomOptions.MinUsers || request.MaxUsers > roomOptions.MaxUsers)
                return new() { Success = false, Message = $"방 이름은 {roomOptions.NameMinLength}~{roomOptions.NameMaxLength}자, 정원은 {roomOptions.MinUsers}~{roomOptions.MaxUsers}명으로 지정해주세요." };
            var room = FindById(request.RoomId);
            if (room == null || room.LobbyId != request.LobbyId)
                return new() { Success = false, Message = "해당 로비에서 방을 찾을 수 없습니다." };
            var caller = userService.FindById(authenticatedUserId);
            if (caller == null || caller.CurrentRoomId != room.Id || !room.Users.ContainsKey(caller.Id) ||
                !connectedTokens.ContainsKey(caller.Id) || room.MasterUserId != caller.Id)
                return new() { Success = false, Message = "현재 방장만 방 설정을 변경할 수 있습니다." };
            ExpireReservations();
            if (room.IsRunning) return new() { Success = false, Message = "진행 중에는 방 설정을 변경할 수 없습니다." };
            if (request.MaxUsers < room.Users.Count + room.ReservedUsers)
                return new() { Success = false, Message = "현재 접속·예약 인원보다 정원을 줄일 수 없습니다." };
            var lobby = lobbyService.FindById(room.LobbyId);
            if (lobby == null || (!roomOptions.AllowDuplicateName && lobby.Rooms.Values.Any(value =>
                value.Id != room.Id && value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                return new() { Success = false, Message = "같은 이름의 방이 이미 존재하거나 로비가 종료되었습니다." };
            room.Name = name;
            room.MaxUsers = request.MaxUsers.Value;
            foreach (var participant in room.Users.Values) participant.CurrentRoomName = name;
            room.LastActiveTime = Now;
            return new() { Success = true, Message = "방 설정을 변경했습니다.", MasterUserId = room.MasterUserId,
                IsEntryLocked = room.IsEntryLocked, RoomName = name, MaxUsers = room.MaxUsers };
        }
    }
}
