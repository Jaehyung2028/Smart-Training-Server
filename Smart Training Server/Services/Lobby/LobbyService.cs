using Smart_Training_Server.Responses.Base;
using System.Collections.Concurrent;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Services.Auth;

// 카탈로그 로비의 저장소와 입장·퇴장 규칙을 관리합니다. 사용자가 로비를 직접 만드는 경로는 없습니다.
namespace Smart_Training_Server.Services.Lobby
{
    public partial class LobbyService
    {
        // 현재 API의 로비 저장소입니다. 키는 로비 ID이며 여러 소속 변경은 별도 공통 잠금으로 보호합니다.
        private readonly ConcurrentDictionary<string, LobbyInfo> lobbies = new();

        private readonly ContentService contentService;

        private readonly UserService userService;

        private readonly Smart_Training_Server.Services.Operations.ServerOperationsRegistry operations;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public LobbyService(
            ContentService contentService,
            UserService userService,
            Smart_Training_Server.Services.Operations.ServerOperationsRegistry? operations = null
        )
        {
            this.contentService = contentService;
            this.userService = userService;
            this.operations = operations ?? new();
        }

        // 메모리 저장소에서 ID에 해당하는 객체를 조회합니다. 결과가 null이면 호출자가 실패 또는 없음으로 처리합니다.
        public LobbyInfo? FindById(string lobbyId)
        {
            lobbies.TryGetValue(lobbyId, out LobbyInfo? lobby);
            return lobby;
        }

        // 내부 로비 객체 목록을 반환합니다. HTTP 응답 형식으로 바꾸는 작업은 조회 서비스와 Controller가 담당합니다.
        public IEnumerable<LobbyInfo> GetAll()
        {
            return lobbies.Values;
        }

        // 초기화된 카탈로그 목록에서 로비를 선택해 입장합니다. ASP.NET에서 소속을 바로 등록합니다.
        // authenticatedUserId는 Controller가 인증 토큰에서 확인한 요청자입니다. request에는 대상과 입력값만 있습니다.
        public Task<JoinLobbyResponse> JoinLobby(
            string authenticatedUserId,
            LobbyMembershipRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (!catalogInitialized)
            {
                return Task.FromResult(new JoinLobbyResponse {
                        Success = false,
                        Message = "로비 목록이 아직 준비되지 않았습니다."
                    });
            }

            lock (userService.MembershipLock)
            {
                return Task.FromResult(JoinCatalogLobby(authenticatedUserId, request, cancellationToken));
            }
        }

        // 공통 잠금 안에서 로비 퇴장을 처리하도록 내부 함수에 위임합니다.
        // authenticatedUserId는 Controller가 인증 토큰에서 확인한 요청자입니다. request에는 대상과 입력값만 있습니다.
        public BaseResponse LeaveLobby(string authenticatedUserId, LobbyMembershipRequest request)
        {
            lock (userService.MembershipLock)
            {
                return LeaveLobbyCore(authenticatedUserId, request);
            }
        }

        // 방 소속이 없는지 확인한 후 현재 로비에서 제거합니다.
        private BaseResponse LeaveLobbyCore(string authenticatedUserId, LobbyMembershipRequest request)
        {
            // 1. 사용자 존재 확인
            UserInfo? user = userService.FindById(authenticatedUserId);
            if (user == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."

                };
            }

            // 2. 로비 존재 확인
            LobbyInfo? lobby = FindById(request.LobbyId);
            if (lobby == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."

                };
            }

            // 3. 해당 로비에 있는지 확인
            if (user.CurrentLobbyId != lobby.Id)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "해당 로비에 입장해 있지 않습니다."

                };
            }

            // 방에서 먼저 퇴장해야 로비와 방의 사용자 상태가 일치합니다.
            if (!string.IsNullOrEmpty(user.CurrentRoomId) || !string.IsNullOrEmpty(user.PendingRoomId))
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "방에서 퇴장하거나 입장 예약을 취소해주세요."

                };
            }

            // 4. 로비에서 제거
            if (!lobby.Users.TryRemove(user.Id, out _))
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "로비 퇴장에 실패했습니다."

                };
            }

            // 5. 사용자 정보 초기화
            user.CurrentLobbyId = string.Empty;
            user.CurrentLobbyName = string.Empty;
            // 6. 마지막 활동 시간 갱신
            lobby.LastActiveTime = DateTime.UtcNow;
            // 7. 성공
            return new BaseResponse
            {
                Success = true,
                Message = "로비에서 퇴장했습니다."

            };
        }

        // 로그아웃·만료 사용자의 로비 소속을 해제하고 사용자에 남은 로비 정보도 초기화합니다.
        internal void RemoveUserFromLobbies(string userId)
        {
            lock (userService.MembershipLock)
            {
                foreach (var lobby in lobbies.Values.ToArray())
                {
                    if (!lobby.Users.TryRemove(userId, out _))
                    {
                        continue;
                    }

                    lobby.LastActiveTime = DateTime.UtcNow;
                }

                var user = userService.FindById(userId);
                if (user != null)
                {
                    user.CurrentLobbyId = string.Empty;
                    user.CurrentLobbyName = string.Empty;
                    user.LobbyIndex = -1;
                }
            }
        }
    }
}
