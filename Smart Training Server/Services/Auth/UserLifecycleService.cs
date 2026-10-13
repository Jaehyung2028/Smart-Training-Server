using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

namespace Smart_Training_Server.Services.Auth;

public sealed class UserLifecycleService(UserService users, RoomService rooms, LobbyService lobbies,
    IHubContext<RoomHub> roomHub, ILogger<UserLifecycleService> logger)
{
    public async Task<BaseResponse> LogoutAsync(string userId)
    {
        IReadOnlyList<RoomDisconnectNotice> notices;
        lock (users.MembershipLock)
        {
            // 동일 사용자에 대한 중복 정리는 안전합니다. 새 로그인은 새 ID를 사용합니다.
            notices = rooms.RemoveUserFromRooms(userId);
            lobbies.RemoveUserFromLobbies(userId);
            users.RemoveUser(userId);
        }
        await SendDisconnectNoticesAsync(notices);
        return new BaseResponse { Success = true, Message = "로그아웃되었습니다." };
    }

    public async Task<int> CleanupExpiredUsersAsync()
    {
        var notices = new List<RoomDisconnectNotice>();
        int removed = 0;
        lock (users.MembershipLock)
        {
            // 갱신과 같은 잠금에서 대상 선정부터 제거까지 수행합니다.
            foreach (var userId in users.GetUsersPastSessionGrace())
            {
                notices.AddRange(rooms.RemoveUserFromRooms(userId));
                lobbies.RemoveUserFromLobbies(userId);
                users.RemoveUser(userId);
                removed++;
            }
        }
        await SendDisconnectNoticesAsync(notices);
        return removed;
    }

    private async Task SendDisconnectNoticesAsync(IReadOnlyList<RoomDisconnectNotice> notices)
    {
        // 네트워크 전송은 잠금 밖에서 처리하며, 실패해도 취소한 토큰을 되살리지 않습니다.
        foreach (var notice in notices)
        {
            try
            {
                await roomHub.Clients.Client(notice.ConnectionId)
                    .SendAsync("DisconnectUser", notice.UserId, notice.AdmissionToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "사용자 {UserId}의 Unity 연결 종료 요청을 전달하지 못했습니다.", notice.UserId);
            }
        }
    }
}
