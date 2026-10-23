using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

// 로그아웃·만료 시 사용자뿐 아니라 방·로비 소속과 세션을 함께 정리하고 Unity 서버에 해제를 통지합니다.
namespace Smart_Training_Server.Services.Auth
{
    public sealed class UserLifecycleService
    {
        private readonly UserService users;
        private readonly RoomService rooms;
        private readonly LobbyService lobbies;
        private readonly IHubContext<RoomHub> roomHub;
        private readonly ILogger<UserLifecycleService> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public UserLifecycleService(
            UserService users,
            RoomService rooms,
            LobbyService lobbies,
            IHubContext<RoomHub> roomHub,
            ILogger<UserLifecycleService> logger
        )
        {
            this.users = users;
            this.rooms = rooms;
            this.lobbies = lobbies;
            this.roomHub = roomHub;
            this.logger = logger;
        }

        // 사용자만 지우면 인원이 남으므로 방·로비·세션을 함께 정리한 뒤 Unity 서버에 접속 해제를 알립니다.
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
            return new BaseResponse
            {
                Success = true,
                Message = "로그아웃되었습니다."
            };
        }

        // 종료 시 로그아웃 요청이 도착하지 않은 사용자도 세션 유예 기한이 지나면 정리합니다.
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

        // 수집한 Unity 서버 연결로 사용자 접속 해제 통지를 전송합니다. 전송 실패가 이미 취소한 세션을 되살리지는 않습니다.
        private async Task SendDisconnectNoticesAsync(IReadOnlyList<RoomDisconnectNotice> notices)
        {
            // 네트워크 전송은 잠금 밖에서 처리하며, 실패해도 취소한 토큰을 되살리지 않습니다.
            foreach (var notice in notices)
            {
                try
                {
                    await roomHub.Clients.Client(notice.ConnectionId).SendAsync(
                        "DisconnectUser",
                        notice.UserId,
                        notice.AdmissionToken
                    );
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "사용자 {UserId}의 Unity 연결 종료 요청을 전달하지 못했습니다.", notice.UserId);
                }
            }
        }
    }
}
