using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Helper;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Services.Lobby
{
    public partial class LobbyService
    {
        private readonly ConcurrentDictionary<string, LobbyInfo> lobbies = new();

        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> readyMap = new();

        private readonly ConcurrentDictionary<string, object> locks = new();

        private readonly ContentService contentService;
        private readonly UserService userService;
        private readonly LobbyOptions lobbyOptions;

        public LobbyService(
            ContentService contentService,
            UserService userService,
            IOptions<LobbyOptions> options)
        {
            this.contentService = contentService;
            this.userService = userService;

            lobbyOptions = options.Value;
        }

        public void OnDisconnected(string lobbyId)
        {
            LobbyInfo? lobby = FindById(lobbyId);

            if (lobby == null)
                return;

            lobby.State = ServerState.Creating;

            Console.WriteLine(
                $"[Lobby] Disconnected : {lobby.Name}");
        }

        public LobbyInfo? FindById(string lobbyId)
        {
            lobbies.TryGetValue(lobbyId, out LobbyInfo? lobby);

            return lobby;
        }

        public bool TryGet(string lobbyId, out LobbyInfo? lobby)
        {
            return lobbies.TryGetValue(lobbyId, out lobby);
        }

        public IEnumerable<LobbyInfo> GetAll()
        {
            return lobbies.Values;
        }

        private bool ExistsLobbyName(string lobbyName)
        {
            return lobbies.Values.Any(lobby =>
                lobby.Name.Equals(lobbyName, StringComparison.OrdinalIgnoreCase));
        }

        private LobbyInfo CreateLobbyInfo(string lobbyName)
        {
            return new LobbyInfo
            {
                Id = Guid.NewGuid().ToString(),

                Name = lobbyName,

                State = ServerState.Creating,

                CreatedAt = DateTime.UtcNow,

                LastActiveTime = DateTime.UtcNow
            };
        }

        public CreateLobbyResponse CreateLobby(CreateLobbyRequest request)
        {
            lock (userService.MembershipLock) return CreateLobbyCore(request);
        }

        private CreateLobbyResponse CreateLobbyCore(CreateLobbyRequest request)
        {
            // 1. 공백 검사
            if (string.IsNullOrWhiteSpace(request.LobbyName))
            {
                return new CreateLobbyResponse
                {
                    Success = false,
                    Message = "로비 이름을 입력해주세요."
                };
            }

            // 2. 최대 로비 개수 검사
            if (lobbies.Count >= lobbyOptions.MaxCount)

            {
                return new CreateLobbyResponse
                {
                    Success = false,
                    Message = "생성 가능한 최대 로비 개수를 초과했습니다."
                };
            }

            // 3. 이름 길이 검사
            if (request.LobbyName.Length < lobbyOptions.NameMinLength ||
                request.LobbyName.Length > lobbyOptions.NameMaxLength)
            {
                return new CreateLobbyResponse
                {
                    Success = false,
                    Message = $"로비 이름은 {lobbyOptions.NameMinLength} ~ {lobbyOptions.NameMaxLength}자 사이여야 합니다."
                };
            }

            // 4. 중복 검사
            if (!lobbyOptions.AllowDuplicateName &&
                ExistsLobbyName(request.LobbyName))
            {
                return new CreateLobbyResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 로비 이름입니다."
                };
            }

            // 5. 로비 생성
            LobbyInfo lobby = CreateLobbyInfo(request.LobbyName);

            // 로비 콘텐츠는 ASP.NET에서 준비하며 Unity 서버 실행과 분리합니다.
            try
            {
                lobby.Contents = contentService.LoadLobbyContents();
            }
            catch (InvalidDataException ex)
            {
                return new CreateLobbyResponse { Success = false, Message = ex.Message };
            }

            // 6. Dictionary 등록
            if (!lobbies.TryAdd(lobby.Id, lobby))
            {
                return new CreateLobbyResponse
                {
                    Success = false,
                    Message = "로비 생성에 실패했습니다."
                };
            }

            // 7. 성공
            return new CreateLobbyResponse
            {
                Success = true,
                Message = "로비가 생성되었습니다.",
                Lobby = lobby
            };
        }

        public Task<JoinLobbyResponse> JoinLobby(JoinLobbyRequest request)
        {
            lock (userService.MembershipLock) return JoinLobbyCore(request);
        }

        private async Task<JoinLobbyResponse> JoinLobbyCore(JoinLobbyRequest request)
        {
            // 1. 사용자 존재 확인
            UserInfo? user = userService.FindById(request.UserId);

            if (user == null)
            {
                return new JoinLobbyResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                };
            }

            // 2. 로비 존재 확인
            LobbyInfo? lobby = FindById(request.LobbyId);

            if (lobby == null)
            {
                return new JoinLobbyResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."
                };
            }

            // 3. 이미 해당 로비에 있는지 확인
            if (user.CurrentLobbyId == lobby.Id)
            {
                return new JoinLobbyResponse
                {
                    Success = false,
                    Message = "이미 해당 로비에 입장해 있습니다."
                };
            }

            // 4. 다른 로비에 입장해 있는지 확인
            if (!string.IsNullOrEmpty(user.CurrentLobbyId))
            {
                return new JoinLobbyResponse
                {
                    Success = false,
                    Message = "이미 다른 로비에 입장해 있습니다."
                };
            }

            // 5. 로비 서버 준비
            //if (!await EnsureLobbyServer(lobby))
            //{
            //    return new JoinLobbyResponse
            //    {
            //        Success = false,
            //        Message = "로비 서버를 시작하지 못했습니다."
            //    };
            //}

            // 6. 로비 사용자 목록에 추가
            if (!lobby.Users.TryAdd(user.Id, user))
            {
                return new JoinLobbyResponse
                {
                    Success = false,
                    Message = "로비 사용자 등록에 실패했습니다."
                };
            }

            // 7. 사용자 현재 로비 정보 갱신
            user.CurrentLobbyId = lobby.Id;

            user.CurrentLobbyName = lobby.Name;

            // 8. 마지막 활동 시간 갱신
            lobby.LastActiveTime = DateTime.UtcNow;

            return new JoinLobbyResponse
            {
                Success = true,
                Message = "로비에 입장했습니다.",
                Lobby = lobby
            };
        }

        private async Task<bool> EnsureLobbyServer(LobbyInfo lobby)
        {
            if (lobby.State == ServerState.Running)
            {
                return true;
            }

            TaskCompletionSource<bool> readyTask;

            object lockObj =
                locks.GetOrAdd(lobby.Id, _ => new object());

            lock (lockObj)
            {
                // 다른 스레드가 먼저 실행했을 수도 있음
                if (lobby.State == ServerState.Running)
                {
                    return true;
                }

                readyTask = CreateReadyTask(lobby);

                if (lobby.State == ServerState.Creating)
                {
                    if (!InitializeLobby(lobby))
                    {
                        return false;
                    }

                    lobby.State = ServerState.Starting;

                    if (!LaunchLobbyServer(lobby))
                    {
                        lobby.State = ServerState.Creating;

                        if (readyMap.TryRemove(lobby.Id, out var cancelTask))
                        {
                            cancelTask.TrySetException(
                                new Exception("로비 서버 실행에 실패했습니다."));
                        }

                        return false;
                    }
                }
            }

            return await WaitLobbyReady(lobby, readyTask);
        }

        private void InitializeNetwork(LobbyInfo lobby)
        {
            lobby.IpAddress = NetworkHelper.GetLocalIPAddress();
            lobby.Port = PortHelper.GetAvailablePort();
        }

        private bool InitializeLobby(LobbyInfo lobby)
        {
            InitializeNetwork(lobby);

            return true;
        }

        private bool LaunchLobbyServer(LobbyInfo lobby)
        {
            try
            {
                ProcessHelper.LaunchUnityServer(
                    lobby.Id,
                    lobby.Port,
                    "LobbyServer/Smart Training Lobby.exe");

                return true;
            }
            catch
            {
                return false;
            }
        }

        private TaskCompletionSource<bool> CreateReadyTask(LobbyInfo lobby)
        {
            return readyMap.GetOrAdd(
                lobby.Id,
                _ => new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private async Task<bool> WaitLobbyReady(
            LobbyInfo lobby,
            TaskCompletionSource<bool> readyTask)
        {
            Task completed =
                await Task.WhenAny(
                    readyTask.Task,
                    Task.Delay(TimeSpan.FromSeconds(60)));

            if (completed == readyTask.Task)
            {
                try
                {
                    await readyTask.Task;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);

                    lobby.State = ServerState.Creating;

                    return false;
                }
            }

            lobby.State = ServerState.Creating;

            readyMap.TryRemove(lobby.Id, out _);

            Console.WriteLine($"[Lobby] READY Timeout : {lobby.Id}");

            return false;
        }

        public void OnReady(string lobbyId)
        {
            if (!lobbies.TryGetValue(lobbyId, out LobbyInfo? lobby))
            {
                return;
            }

            lobby.State = ServerState.Running;

            CompleteReadyTask(lobbyId);

            lobby.LastActiveTime = DateTime.UtcNow;
        }

        private void CompleteReadyTask(string lobbyId)
        {
            if (readyMap.TryRemove(lobbyId, out TaskCompletionSource<bool>? readyTask))
            {
                readyTask.TrySetResult(true);
            }
        }

        public LeaveLobbyResponse LeaveLobby(LeaveLobbyRequest request)
        {
            lock (userService.MembershipLock) return LeaveLobbyCore(request);
        }

        private LeaveLobbyResponse LeaveLobbyCore(LeaveLobbyRequest request)
        {
            // 1. 사용자 존재 확인
            UserInfo? user = userService.FindById(request.UserId);

            if (user == null)
            {
                return new LeaveLobbyResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                };
            }

            // 2. 로비 존재 확인
            LobbyInfo? lobby = FindById(request.LobbyId);

            if (lobby == null)
            {
                return new LeaveLobbyResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."
                };
            }

            // 3. 해당 로비에 있는지 확인
            if (user.CurrentLobbyId != lobby.Id)
            {
                return new LeaveLobbyResponse
                {
                    Success = false,
                    Message = "해당 로비에 입장해 있지 않습니다."
                };
            }

            // 방에서 먼저 퇴장해야 로비와 방의 사용자 상태가 일치합니다.
            if (!string.IsNullOrEmpty(user.CurrentRoomId) || !string.IsNullOrEmpty(user.PendingRoomId))
            {
                return new LeaveLobbyResponse
                {
                    Success = false,
                    Message = "방에서 퇴장하거나 입장 예약을 취소해주세요."
                };
            }

            // 4. 로비에서 제거
            if (!lobby.Users.TryRemove(user.Id, out _))
            {
                return new LeaveLobbyResponse
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

            if (lobby.Users.IsEmpty && lobby.Rooms.IsEmpty)
            {
                DeleteLobby(lobby);
            }

            // 7. 성공
            return new LeaveLobbyResponse
            {
                Success = true,
                Message = "로비에서 퇴장했습니다."
            };
        }

        private bool DeleteLobby(LobbyInfo lobby)
        {
            return lobbies.TryRemove(lobby.Id, out _);
        }

        internal void RemoveUserFromLobbies(string userId)
        {
            lock (userService.MembershipLock)
            {
                foreach (var lobby in lobbies.Values.ToArray())
                {
                    if (!lobby.Users.TryRemove(userId, out _)) continue;
                    lobby.LastActiveTime = DateTime.UtcNow;
                    if (lobby.Users.IsEmpty && lobby.Rooms.IsEmpty) DeleteLobby(lobby);
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

        internal void RemoveEmptyLobby(string lobbyId)
        {
            lock (userService.MembershipLock)
            {
                var lobby = FindById(lobbyId);
                if (lobby != null && lobby.Users.IsEmpty && lobby.Rooms.IsEmpty) DeleteLobby(lobby);
            }
        }
    }
}
