using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Room;
public partial class RoomService
{
    public BaseResponse UpdateRoomPassword(string userId, UpdateRoomPasswordRequest request)
    {
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrWhiteSpace(request.LobbyId) || string.IsNullOrWhiteSpace(request.RoomId) ||
                request.Password == null || !RoomPassword.Valid(request.Password))
                return Result(false, "비밀번호는 4~32자로 입력하거나 해제할 때 빈 문자열을 지정해주세요.");
            var room = FindById(request.RoomId);
            var caller = string.IsNullOrWhiteSpace(userId) ? null : userService.FindById(userId);
            if (room == null || room.LobbyId != request.LobbyId) return Result(false, "방을 찾을 수 없습니다.");
            if (room.IsClosing || caller == null || caller.CurrentRoomId != room.Id || room.MasterUserId != userId ||
                !room.Users.ContainsKey(userId) || !connectedTokens.ContainsKey(userId))
                return Result(false, "현재 접속한 방장만 비밀번호를 변경할 수 있습니다.");
            RoomPassword.Set(room, request.Password);
            int canceled = 0;
            foreach (var admission in reservations.Values.Where(value => value.RoomId == room.Id).ToArray())
            { RemoveReservation(admission); canceled++; }
            room.LastActiveTime = Now;
            return Result(true, (room.HasPassword ? "방 비밀번호를 설정·변경했습니다." : "방 비밀번호를 해제했습니다.") +
                (canceled > 0 ? $" 기존 입장 예약 {canceled}개를 취소했습니다." : ""));
        }
    }
}
