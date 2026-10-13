using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Helper;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        private readonly ConcurrentDictionary<string, RoomInfo> rooms = new();

        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> readyMap = new();

        private readonly LobbyService lobbyService;
        private readonly UserService userService;
        private readonly RoomOptions roomOptions;
        private readonly IRoomServerProcessManager processManager;

        public RoomService(LobbyService lobbyService, 
            UserService userService, 
            IOptions<RoomOptions> options, TimeProvider? timeProvider = null,
            IRoomServerProcessManager? processManager = null)
        {
            this.lobbyService = lobbyService;
            this.userService = userService;

            roomOptions = options.Value;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.processManager = processManager ?? new RoomServerProcessManager(roomOptions.ServerApiBaseUrl);
            if (string.IsNullOrWhiteSpace(roomOptions.ServerExecutablePath) || roomOptions.ServerReadyTimeoutSeconds is < 1 or > 300)
                throw new ArgumentOutOfRangeException(nameof(options), "서버 경로와 1~300초의 준비 대기 시간을 설정해주세요.");
        }

        public void OnDisconnected(string roomId)
        {
            RoomInfo? room = FindById(roomId);

            if (room == null)
                return;

            room.State = ServerState.Creating;

            Console.WriteLine(
                $"[Room] Disconnected : {room.Name}");
        }

        public RoomInfo? FindById(string roomId)
        {
            rooms.TryGetValue(roomId, out RoomInfo? room);

            return room;
        }

        public RoomInfo? FindById(LobbyInfo lobby, string roomId)
        {
            lobby.Rooms.TryGetValue(roomId, out RoomInfo? room);

            return room;
        }

        public bool TryGet(string roomId, out RoomInfo? room)
        {
            return rooms.TryGetValue(roomId, out room);
        }

        public IEnumerable<RoomInfo> GetAll()
        {
            return rooms.Values;
        }

        private bool ExistsRoomName(LobbyInfo lobby, string roomName)
        {
            return lobby.Rooms.Values.Any(room =>
                room.Name.Equals(
                    roomName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private RoomInfo CreateRoomInfo(
            LobbyInfo lobby,
            UserInfo masterUser,
            ContentInfo content,
            CreateRoomRequest request)
        {
            return new RoomInfo
            {
                Id = Guid.NewGuid().ToString(),

                LobbyId = lobby.Id,

                Name = request.RoomName,

                State = ServerState.Creating,

                Content = content,

                MaxUsers = request.MaxUsers,

                MasterUserId = masterUser.Id,
                CreatorUserId = masterUser.Id,

                CreatedAt = DateTime.UtcNow,

                LastActiveTime = DateTime.UtcNow
            };
        }

        public CreateRoomResponse CreateRoom(CreateRoomRequest request)
        {
            lock (userService.MembershipLock) return CreateRoomCore(request);
        }

        private CreateRoomResponse CreateRoomCore(CreateRoomRequest request)
        {
            // 1. 방 이름 공백 검사
            if (string.IsNullOrWhiteSpace(request.RoomName))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "방 이름을 입력해주세요."
                };
            }

            // 2. 사용자 존재 확인
            UserInfo? user = userService.FindById(request.UserId);

            if (user == null)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                };
            }

            // 3. 로비 존재 확인
            LobbyInfo? lobby = lobbyService.FindById(request.LobbyId);

            if (lobby == null)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."
                };
            }

            // 4. 사용자가 해당 로비에 접속 중인지 확인
            if (user.CurrentLobbyId != lobby.Id)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "해당 로비에 입장한 사용자만 방을 생성할 수 있습니다."
                };
            }

            // 5. 방 이름 길이 검사
            if (request.RoomName.Length < roomOptions.NameMinLength ||
                request.RoomName.Length > roomOptions.NameMaxLength)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message =
                        $"방 이름은 {roomOptions.NameMinLength} ~ {roomOptions.NameMaxLength}자 사이여야 합니다."
                };
            }

            // 6. 방 이름 중복 검사
            if (!roomOptions.AllowDuplicateName &&
                ExistsRoomName(lobby, request.RoomName))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 방 이름입니다."
                };
            }

            // 7. 최대 인원 검사
            if (request.MaxUsers < roomOptions.MinUsers ||
                request.MaxUsers > roomOptions.MaxUsers)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message =
                        $"방 최대 인원은 {roomOptions.MinUsers} ~ {roomOptions.MaxUsers}명 사이여야 합니다."
                };
            }

            // 8. 콘텐츠 존재 확인
            if (!lobby.Contents.TryGetValue(
                request.ContentId,
                out ContentInfo? content))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "콘텐츠를 찾을 수 없습니다."
                };
            }

            // 9. 방 생성
            RoomInfo room = CreateRoomInfo(
                lobby,
                user,
                content,
                request);

            if(!rooms.TryAdd(room.Id, room))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "방 생성에 실패했습니다."
                };
            }

            // 10. 로비의 방 목록에 등록
            if (!lobby.Rooms.TryAdd(room.Id, room))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "방 생성에 실패했습니다."
                };
            }

            // 11. 로비 마지막 활동 시간 갱신
            lobby.LastActiveTime = DateTime.UtcNow;

            return new CreateRoomResponse
            {
                Success = true,
                Message = "방이 생성되었습니다.",
                Room = room
            };
        }

        private async Task<bool> EnsureRoomServer(RoomInfo room)
        {
            TaskCompletionSource<bool> readyTask;

            lock (userService.MembershipLock)
            {
                if (FindById(room.Id) != room || room.ReservedUsers == 0) return false;
                if (room.State == ServerState.Running)
                {
                    return true;
                }

                readyTask = CreateReadyTask(room);

                if (room.State == ServerState.Creating)
                {
                    try
                    {
                        InitializeRoom(room);
                        room.State = ServerState.Starting;
                        if (!LaunchRoomServer(room))
                        {
                            ResetRoomServer(room, "실행 실패");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Room] 초기화 실패 ({room.Id}): {ex.Message}");
                        ResetRoomServer(room, "초기화 실패");
                        return false;
                    }
                }
            }

            return await WaitRoomReady(room, readyTask);
        }

        private void InitializeNetwork(RoomInfo room)
        {
            room.IpAddress = string.IsNullOrEmpty(roomOptions.AdvertisedAddress)
                ? NetworkHelper.GetLocalIPAddress()
                : roomOptions.AdvertisedAddress;

            room.Port = PortHelper.GetAvailablePort();
        }

        private bool InitializeRoom(RoomInfo room)
        {
            InitializeNetwork(room);
            room.ServerToken = NewToken();

            room.LastActiveTime = DateTime.UtcNow;

            return true;
        }

        private bool LaunchRoomServer(RoomInfo room)
        {
            try
            {
                processManager.Launch(
                    room.Id,
                    room.Port,
                    roomOptions.ServerExecutablePath, room.ServerToken);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Room] 실행 실패 ({room.Id}): {ex.Message}");
                return false;
            }
        }

        private TaskCompletionSource<bool> CreateReadyTask(RoomInfo room)
        {
            return readyMap.GetOrAdd(
                room.Id,
                _ => new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private async Task<bool> WaitRoomReady(
    RoomInfo room,
    TaskCompletionSource<bool> readyTask)
        {
            Task completed =
                await Task.WhenAny(
                    readyTask.Task,
                    Task.Delay(TimeSpan.FromSeconds(roomOptions.ServerReadyTimeoutSeconds)));

            if (completed == readyTask.Task)
            {
                try
                {
                    return await readyTask.Task;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);

                    room.State = ServerState.Creating;

                    return false;
                }
            }

            lock (userService.MembershipLock)
            {
                if (room.State == ServerState.Running) return true;
                if (readyMap.TryGetValue(room.Id, out var current) && current == readyTask)
                {
                    ResetRoomServer(room, "준비 대기 시간 초과");
                }
            }

            Console.WriteLine($"[Room] READY Timeout : {room.Id}");

            return false;
        }

        public void OnReady(string roomId)
        {
            RoomInfo? room = FindById(roomId);

            if (room == null)
            {
                return;
            }

            room.State = ServerState.Running;

            room.LastActiveTime = DateTime.UtcNow;

            CompleteReadyTask(roomId);
        }

        private void CompleteReadyTask(string roomId)
        {
            if (readyMap.TryRemove(roomId, out var readyTask))
            {
                readyTask.TrySetResult(true);
            }
        }

        public LeaveRoomResponse LeaveRoom(LeaveRoomRequest request)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                if (reservations.TryGetValue(request.UserId, out var pending) &&
                    pending.RoomId == request.RoomId && FindById(pending.RoomId)?.LobbyId == request.LobbyId)
                {
                    RemoveReservation(pending);
                    return new LeaveRoomResponse { Success = true, Message = "입장 예약이 취소되었습니다." };
                }
                return LeaveRoomCore(request);
            }
        }

        private LeaveRoomResponse LeaveRoomCore(LeaveRoomRequest request)
        {
            UserInfo? user = userService.FindById(request.UserId);

            if (user == null)
            {
                return new LeaveRoomResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                };
            }

            LobbyInfo? lobby =
    lobbyService.FindById(request.LobbyId);

            if (lobby == null)
            {
                return new LeaveRoomResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."
                };
            }

            RoomInfo? room = FindById(lobby, request.RoomId);

            if (room == null)
            {
                return new LeaveRoomResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다."
                };
            }

            if (user.CurrentRoomId != room.Id)
            {
                return new LeaveRoomResponse
                {
                    Success = false,

                    Message = "해당 방에 입장해 있지 않습니다."
                };
            }

            if (!room.Users.TryRemove(user.Id, out _))
            {
                return new LeaveRoomResponse
                {
                    Success = false,
                    Message = "방 퇴장에 실패했습니다."
                };
            }

            user.CurrentRoomId = string.Empty;

            user.CurrentRoomName = string.Empty;
            user.RoomIndex = -1;
            connectedTokens.Remove(user.Id);

            room.LastActiveTime = DateTime.UtcNow;

            lobby.LastActiveTime = DateTime.UtcNow;

            // 마지막 사용자
            if (room.Users.IsEmpty && room.ReservedUsers == 0)
            {
                DeleteRoom(lobby, room);
            }
            else if (room.MasterUserId == user.Id)
            {
                ChangeMaster(room);
            }

            // 7. 성공
            return new LeaveRoomResponse
            {
                Success = true,
                Message = "방에서 퇴장했습니다."
            };
        }

        private void ChangeMaster(RoomInfo room)
        {
            if (room.Users.IsEmpty)
            {
                room.MasterUserId = string.Empty;
                return;
            }

            UserInfo newMaster = room.Users.Values
                .OrderBy(x => x.RoomIndex)
                .First();

            room.MasterUserId = newMaster.Id;
        }

        private bool DeleteRoom(
    LobbyInfo? lobby,
    RoomInfo room)
        {
            // 실행 중인 서버 종료
            if (room.State == ServerState.Running || room.State == ServerState.Starting)
            {
                processManager.Stop(room.Id);
            }

            room.State = ServerState.Stopped;

            // READY 대기 제거
            if (readyMap.TryRemove(room.Id, out var readyTask))
            {
                readyTask.TrySetCanceled();
            }

            serverConnections.Remove(room.Id);
            room.ServerToken = string.Empty;

            // 전역 Room 제거
            rooms.TryRemove(room.Id, out _);

            // Lobby 소속 Room 제거
            lobby?.Rooms.TryRemove(room.Id, out _);

            Console.WriteLine($"[Room 삭제] {room.Name}");

            return true;
        }
    }
}
