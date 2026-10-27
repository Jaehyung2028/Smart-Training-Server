using Smart_Training_Server.Responses.Base;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Helper;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;

// 방 생성·퇴장과 전용 서버 준비 절차를 담당합니다. 다른 RoomService.* 파일들과 동일한 저장소와 잠금을 사용합니다.
namespace Smart_Training_Server.Services.Room
{
    // 방 정보와 입장·퇴장 규칙을 관리합니다. RoomService.*.cs는 기능별로 나눈 동일 클래스입니다.
    public partial class RoomService
    {
        // API가 알고 있는 방 정보 저장소입니다. Unity 프로세스 자체는 processManager가 관리합니다.
        private readonly ConcurrentDictionary<string, RoomInfo> rooms = new();

        // 방마다 준비 완료 신호를 공유합니다. 프로세스 실행 성공과 실제 접속 준비 완료는 구분합니다.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> readyMap = new();

        private readonly LobbyService lobbyService;

        private readonly UserService userService;

        private readonly ILogger<RoomService> logger;

        private readonly RoomOptions roomOptions;

        private readonly IRoomServerProcessManager processManager;

        private readonly Smart_Training_Server.Services.Operations.ServerOperationsRegistry operations;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public RoomService(
            LobbyService lobbyService,
            UserService userService,
            IOptions<RoomOptions> options,
            TimeProvider? timeProvider = null,
            IRoomServerProcessManager? processManager = null,
            Smart_Training_Server.Services.Operations.ServerOperationsRegistry? operations = null,
            ILogger<RoomService>? logger = null
        )
        {
            this.lobbyService = lobbyService;
            this.userService = userService;
            this.logger = logger ?? NullLogger<RoomService>.Instance;
            roomOptions = options.Value;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.processManager = processManager ?? new RoomServerProcessManager(roomOptions.ServerApiBaseUrl);
            this.operations = operations ?? new();
            if (string.IsNullOrWhiteSpace(roomOptions.ServerExecutablePath) ||
                roomOptions.ServerReadyTimeoutSeconds is < 1 or > 300)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "서버 경로와 1~300초의 준비 대기 시간을 설정해주세요.");
            }
        }

        // 메모리 저장소에서 ID에 해당하는 객체를 조회합니다. 결과가 null이면 호출자가 실패 또는 없음으로 처리합니다.
        public RoomInfo? FindById(string roomId)
        {
            rooms.TryGetValue(roomId, out RoomInfo? room);
            return room;
        }

        // 메모리 저장소에서 ID에 해당하는 객체를 조회합니다. 결과가 null이면 호출자가 실패 또는 없음으로 처리합니다.
        public RoomInfo? FindById(LobbyInfo lobby, string roomId)
        {
            lobby.Rooms.TryGetValue(roomId, out RoomInfo? room);
            return room;
        }

        // 같은 로비 안에서 동일한 방 이름이 있는지 영문 대소문자를 구분하지 않고 확인합니다.
        private bool ExistsRoomName(LobbyInfo lobby, string roomName)
        {
            return lobby.Rooms.Values.Any(room => room.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase));
        }

        // 방의 고유 ID·소속 로비·콘텐츠·정원·최초 생성자와 기본 시각을 초기화합니다.
        // 카탈로그의 모든 깊이를 탐색하되 전달받은 루트 안에서만 검색합니다.
        private static ContentInfo? FindContentNode(ContentInfo content, int nodeId)
        {
            if (content.Id == nodeId)
            {
                return content;
            }

            foreach (var child in content.Children)
            {
                var found = FindContentNode(child, nodeId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private RoomInfo CreateRoomInfo(LobbyInfo lobby, UserInfo masterUser, ContentInfo content, CreateRoomRequest request)
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
                CreatedAt = DateTime.UtcNow,
                LastActiveTime = DateTime.UtcNow

            };
        }

        // 같은 로비의 방 이름과 사용자 상태를 확인하고 방 정보를 생성합니다.
        // authenticatedUserId는 Controller가 인증 토큰에서 확인한 요청자입니다. request에는 대상과 입력값만 있습니다.
        public CreateRoomResponse CreateRoom(string authenticatedUserId, CreateRoomRequest request)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var user = userService.FindById(authenticatedUserId);
                if (user != null && (user.CurrentRoomId.Length != 0 || user.PendingRoomId.Length != 0))
                {
                    return new CreateRoomResponse
                    {
                        Success = false,
                        Message = "이미 방에 입장했거나 입장을 예약했습니다."

                    };
                }

                var response = CreateRoomCore(authenticatedUserId, request);
                if (response.Success && response.Room != null && user != null)
                {
                    // 방 등록과 생성자의 자리 확보를 같은 잠금에서 완료합니다. 응답이 유실되어도 예약 만료로 정리됩니다.
                    ReserveRoom(user, response.Room, awaitingJoinRequest: true);
                }

                return response;
            }
        }

        // 이름·비밀번호·정원·소속·콘텐츠 등을 검사한 뒤 방 정보를 등록합니다. 관련 상태 변경은 공통 잠금 안에서 수행합니다.
        private CreateRoomResponse CreateRoomCore(string authenticatedUserId, CreateRoomRequest request)
        {
            if (!RoomPassword.Valid(request.Password))
            {
                return new()
                {
                    Success = false,
                    Message = "방 비밀번호는 4~32자이며 공백만 사용할 수 없습니다."

                };
            }

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
            UserInfo? user = userService.FindById(authenticatedUserId);
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
                    Message = $"방 이름은 {roomOptions.NameMinLength} ~ {roomOptions.NameMaxLength}자 사이여야 합니다."
                };
            }

            // 6. 방 이름 중복 검사
            if (!roomOptions.AllowDuplicateName && ExistsRoomName(lobby, request.RoomName))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 방 이름입니다."

                };
            }

            // 7. 최대 인원 검사
            if (request.MaxUsers < roomOptions.MinUsers || request.MaxUsers > roomOptions.MaxUsers)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = $"방 최대 인원은 {roomOptions.MinUsers} ~ {roomOptions.MaxUsers}명 사이여야 합니다."
                };
            }

            // 8. 콘텐츠 존재 확인
            if (!lobby.Contents.TryGetValue(request.ContentId, out ContentInfo? content))
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "콘텐츠를 찾을 수 없습니다."

                };
            }

            // 루트 문자열은 트리를, 숫자 ID는 그 안에서 선택한 노드를 구분합니다.
            // 다른 로비의 노드나 없는 ID를 요청해도 해당 트리 밖에서는 찾지 않습니다.
            if (request.ContentNodeId != 0)
            {
                content = FindContentNode(content, request.ContentNodeId);
                if (content == null)
                {
                    return new CreateRoomResponse
                    {
                        Success = false,
                        Message = "선택한 하위 콘텐츠를 찾을 수 없습니다."

                    };
                }
            }

            // 부모 콘텐츠는 분류용입니다. 클라이언트 UI를 우회해 요청해도 최하위 콘텐츠만 허용합니다.
            if (content.Children.Count > 0)
            {
                return new CreateRoomResponse
                {
                    Success = false,
                    Message = "하위 항목이 없는 최하위 콘텐츠를 선택해주세요."
                };
            }

            // 9. 방 생성
            RoomInfo room = CreateRoomInfo(lobby, user, content, request);
            RoomPassword.Set(room, request.Password);
            if (!rooms.TryAdd(room.Id, room))
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

        // authenticatedUserId는 Controller가 인증 토큰에서 확인한 요청자입니다. request에는 대상과 입력값만 있습니다.
        public BaseResponse LeaveRoom(string authenticatedUserId, RoomMembershipRequest request)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                if (reservations.TryGetValue(authenticatedUserId, out var pending) &&
                    pending.RoomId == request.RoomId &&
                    FindById(pending.RoomId)?.LobbyId == request.LobbyId)
                {
                    RemoveReservation(pending);
                    return new BaseResponse
                    {
                        Success = true,
                        Message = "입장 예약이 취소되었습니다."

                    };
                }

                return LeaveRoomCore(authenticatedUserId, request);
            }
        }

        // 실제 참가자의 소속을 해제하고 남은 인원에 맞춰 방장과 방 정리를 처리합니다.
        private BaseResponse LeaveRoomCore(string authenticatedUserId, RoomMembershipRequest request)
        {
            UserInfo? user = userService.FindById(authenticatedUserId);
            if (user == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."

                };
            }

            LobbyInfo? lobby = lobbyService.FindById(request.LobbyId);
            if (lobby == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "로비를 찾을 수 없습니다."

                };
            }

            RoomInfo? room = FindById(lobby, request.RoomId);
            if (room == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "방을 찾을 수 없습니다."

                };
            }

            if (user.CurrentRoomId != room.Id)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "해당 방에 입장해 있지 않습니다."

                };
            }

            if (!room.Users.TryRemove(user.Id, out _))
            {
                return new BaseResponse
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
            return new BaseResponse
            {
                Success = true,
                Message = "방에서 퇴장했습니다."

            };
        }

        // 기존 방장이 나간 경우 남은 참가자에게 방장 권한을 위임합니다.
        private void ChangeMaster(RoomInfo room)
        {
            if (room.Users.IsEmpty)
            {
                room.MasterUserId = string.Empty;
                return;
            }

            UserInfo newMaster = room.Users.Values.OrderBy(x => x.RoomIndex).First();
            room.MasterUserId = newMaster.Id;
        }

        // 방 서버와 관련 저장소를 정리하고 소속 로비의 방 목록에서도 제거합니다.
        private bool DeleteRoom(LobbyInfo? lobby, RoomInfo room)
        {
            // 실행 중인 서버 종료
            StopRoomProcess(room);
            room.State = ServerState.Stopped;
            operations.Record("room", room.Id, room.LobbyId, room.Name, "stopped", "방 삭제");
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
            logger.LogInformation("방 삭제: {RoomId}, {RoomName}", room.Id, room.Name);
            return true;
        }
    }
}
