using System.Text.RegularExpressions;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Lobby;

// 카탈로그의 고정 로비를 등록하고 사용자 소속을 관리합니다. 로비 전용 Unity 프로세스는 사용하지 않습니다.
namespace Smart_Training_Server.Services.Lobby
{
    public partial class LobbyService
    {
        private bool catalogInitialized;

        // 콘텐츠별 로비 목록을 한 번 구성합니다. 비어 있는 로비도 목록에서 유지합니다.
        public void InitializeCatalogLobbies()
        {
            var contents = contentService.LoadLobbyContents();
            foreach (var content in contents.Values)
            {
                if (!Regex.IsMatch(content.RootContent, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
                {
                    throw new InvalidDataException("catalog.json의 rootContent는 영문·숫자·점·밑줄·하이픈의 1~64자 ID여야 합니다.");
                }
            }

            lock (userService.MembershipLock)
            {
                if (catalogInitialized)
                {
                    return;
                }

                foreach (var content in contents.Values.OrderBy(x => x.RootContent, StringComparer.Ordinal))
                {
                    var lobby = new LobbyInfo
                    {
                        Id = content.RootContent,
                        Name = content.Title,
                        CreatedAt = DateTime.UtcNow,
                        LastActiveTime = DateTime.UtcNow

                    };
                    lobby.Contents[content.RootContent] = content;
                    lobbies[lobby.Id] = lobby;
                }

                catalogInitialized = true;
            }
        }

        // MembershipLock 안에서 검사와 소속 등록을 함께 끝냅니다. 프로세스 준비 대기는 없습니다.
        private JoinLobbyResponse JoinCatalogLobby(string userId, LobbyMembershipRequest request, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                return Error("로비 입장을 취소했습니다.");
            }

            var user = userService.FindById(userId);
            var lobby = FindById(request.LobbyId);
            if (user == null || lobby == null)
            {
                return Error("사용자 또는 로비를 찾을 수 없습니다.");
            }

            if (user.CurrentLobbyId.Length != 0)
            {
                return Error("이미 로비에 입장했습니다.");
            }

            var shutdown = operations.GetShutdown(lobby.Id);
            if (shutdown.State != "none")
            {
                return Error("종료 예정이거나 종료된 로비입니다. " + shutdown.Reason);
            }

            var maintenance = operations.GetMaintenance(lobby.Id);
            if (maintenance.Enabled)
            {
                return Error("점검 중인 로비입니다. " + maintenance.Reason);
            }

            lobby.Users[user.Id] = user;
            user.CurrentLobbyId = lobby.Id;
            user.CurrentLobbyName = lobby.Name;
            lobby.LastActiveTime = DateTime.UtcNow;
            return new JoinLobbyResponse
            {
                Success = true,
                Message = "로비에 입장했습니다.",
                Lobby = lobby

            };
        }

        private static JoinLobbyResponse Error(string message)
        {
            return new JoinLobbyResponse
            {
                Success = false,
                Message = message

            };
        }
    }
}
