using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public async Task<BaseResponse> KickRoomUserAsync(string authenticatedUserId, KickRoomUserRequest request,
        IHubContext<RoomHub> roomHub)
    {
        RoomDisconnectNotice notice;
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrWhiteSpace(request.LobbyId) || string.IsNullOrWhiteSpace(request.RoomId) ||
                string.IsNullOrWhiteSpace(request.TargetUserId)) return Result(false, "로비, 방, 강퇴할 참가자를 지정해주세요.");
            var room = FindById(request.RoomId);
            if (room == null || room.LobbyId != request.LobbyId) return Result(false, "해당 로비에서 방을 찾을 수 없습니다.");
            var caller = userService.FindById(authenticatedUserId);
            if (caller == null || caller.CurrentRoomId != room.Id || !room.Users.ContainsKey(caller.Id) ||
                !connectedTokens.ContainsKey(caller.Id) || room.MasterUserId != caller.Id)
                return Result(false, "현재 방장만 참가자를 강퇴할 수 있습니다.");
            if (request.TargetUserId == caller.Id) return Result(false, "자신은 강퇴할 수 없습니다. 방에서 나가기를 이용해주세요.");
            var target = userService.FindById(request.TargetUserId);
            if (target == null || target.CurrentRoomId != room.Id || !room.Users.ContainsKey(target.Id) ||
                !connectedTokens.TryGetValue(target.Id, out var token))
                return Result(false, "같은 방에 접속 중인 참가자만 강퇴할 수 있습니다.");
            if (!serverConnections.TryGetValue(room.Id, out var connection)) return Result(false, "방 서버 연결을 확인할 수 없습니다.");
            notice = new(connection, target.Id, token);
        }
        // Keep the slot occupied until the dedicated server confirms actual disconnection.
        // The admission token prevents a delayed kick from affecting a new connection.
        try
        {
            await roomHub.Clients.Client(notice.ConnectionId).SendAsync("KickUser", notice.UserId, notice.AdmissionToken)
                .WaitAsync(TimeSpan.FromSeconds(5));
            return Result(true, "강퇴 요청을 전달했습니다.");
        }
        catch (Exception)
        {
            return Result(false, "강퇴 요청을 전달하지 못했습니다. 접속 상태를 확인한 뒤 다시 시도해주세요.");
        }
    }
}
