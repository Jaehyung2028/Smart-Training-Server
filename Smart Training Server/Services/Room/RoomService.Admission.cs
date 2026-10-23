using System.Security.Cryptography;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Room;

// 방 입장 예약·서버 등록·연결 확인·연결 종료·예약 만료를 처리합니다. HTTP 예약 성공과 실제 멀티 연결 완료를 구분합니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 사용자 ID별 입장 예약입니다. 한 사용자가 여러 방의 자리를 동시에 예약하지 못하도록 관리합니다.
        private readonly Dictionary<string, RoomAdmission> reservations = new();
        // 실제 접속 중인 사용자 ID와 당시 입장 토큰을 연결합니다. 늦게 도착한 이전 접속의 보고를 구분합니다.
        private readonly Dictionary<string, string> connectedTokens = new();
        // 방 ID와 등록된 Unity 서버의 SignalR 연결 ID를 연결합니다.
        private readonly Dictionary<string, string> serverConnections = new();
        private readonly TimeProvider timeProvider;

        // TimeProvider로 현재 UTC 시각을 얻습니다. 예약 만료와 활동 시각에 같은 시간 기준을 적용합니다.
        private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

        // 추측하기 어려운 난수 토큰을 생성합니다. 사용자 인증과 별개로 예약·서버 등록에 사용합니다.
        private static string NewToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        private static JoinRoomResponse JoinError(string message)
        {
            return new()
            {
                Success = false,
                Message = message
            };
        }

        // 이 함수의 성공은 '자리 예약 완료'입니다. 실제 참가자 등록은 Netcode 연결 후 ConfirmConnection에서 합니다.
        // authenticatedUserId는 Controller가 인증 토큰에서 확인한 요청자입니다. request에는 대상과 입력값만 있습니다.
        public async Task<JoinRoomResponse> JoinRoom(
            string authenticatedUserId,
            JoinRoomRequest request,
            CancellationToken cancellationToken = default
        )
        {
            RoomAdmission admission;
            RoomInfo room;
            lock (userService.MembershipLock)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return JoinError("입장을 취소했습니다.");
                }

                ExpireReservations();
                var user = userService.FindById(authenticatedUserId);
                var lobby = lobbyService.FindById(request.LobbyId);
                var found = lobby == null ? null : FindById(lobby, request.RoomId);
                if (user == null || lobby == null || found == null)
                {
                    return JoinError("사용자, 로비 또는 방을 찾을 수 없습니다.");
                }

                room = found;
                if (room.IsClosing)
                {
                    return JoinError("방장이 방을 닫고 있습니다.");
                }

                if (!RoomPassword.Matches(room, request.Password))
                {
                    return JoinError("방 비밀번호가 올바르지 않습니다.");
                }

                if (user.CurrentLobbyId != lobby.Id)
                {
                    return JoinError("해당 로비에 먼저 입장해주세요.");
                }

                if (room.IsRunning)
                {
                    return JoinError("진행 중인 방에는 입장할 수 없습니다.");
                }

                if (room.IsEntryLocked)
                {
                    return JoinError("방장이 새 참가자의 입장을 잠갔습니다.");
                }

                if (user.CurrentRoomId.Length != 0)
                {
                    return JoinError("이미 방에 입장했거나 입장을 예약했습니다.");
                }

                if (reservations.TryGetValue(user.Id, out var existing))
                {
                    // 생성과 함께 확보한 예약만 최초 입장 요청에서 이어받습니다. 정원도 두 번 차감하지 않습니다.
                    if (existing.RoomId != room.Id || !existing.AwaitingJoinRequest)
                    {
                        return JoinError("이미 방에 입장했거나 입장을 예약했습니다.");
                    }

                    admission = existing;
                    admission.AwaitingJoinRequest = false;
                    // 생성 후 기다린 시간과 별개로 이번 서버 준비에 필요한 예약 기한을 다시 부여합니다.
                    admission.ExpiresAtUtc = Now.AddSeconds(roomOptions.ServerReadyTimeoutSeconds + 60);
                }
                else
                {
                    if (user.PendingRoomId.Length != 0)
                    {
                        return JoinError("이미 방에 입장했거나 입장을 예약했습니다.");
                    }

                    // 연결이 진행 중인 예약 자리도 정원에 포함해 동시 요청의 초과 입장을 막습니다.
                    if (room.Users.Count + room.ReservedUsers >= room.MaxUsers)
                    {
                        return JoinError("방이 가득 찼습니다.");
                    }

                    admission = ReserveRoom(user, room);
                }
            }

            bool ready;
            // 서버 준비 대기는 잠금 밖에서 수행합니다. 그동안 다른 사용자의 입장·취소 요청도 처리할 수 있습니다.
            try
            {
                ready = await EnsureRoomServer(room).WaitAsync(cancellationToken);
            }
            catch
            {
                ready = false;
            }

            lock (userService.MembershipLock)
            {
                ExpireReservations();
                // 서버를 기다리는 동안 예약이 취소되거나 교체될 수 있습니다. 처음 만든 바로 그 예약인지 다시 확인합니다.
                if (!reservations.TryGetValue(admission.UserId, out var current) || current != admission)
                {
                    RemoveEmptyRoom(room);
                    return JoinError("입장 예약이 취소되거나 만료되었습니다.");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    RemoveReservation(admission);
                    return JoinError("입장을 취소했습니다.");
                }

                if (!ready || room.State != ServerState.Running || FindById(room.Id) != room)
                {
                    RemoveReservation(admission);
                    return JoinError("방 서버를 시작하지 못했습니다.");
                }

                // 접속 정보가 반환된 시점부터 Netcode 접속에 30초를 부여합니다.
                admission.ExpiresAtUtc = Now.AddSeconds(30);
                return new JoinRoomResponse
                {
                    Success = true,
                    Message = "입장이 예약되었습니다. Unity 서버에 접속해주세요.",
                    Room = room,
                    AdmissionToken = admission.Token,
                    ExpiresAtUtc = admission.ExpiresAtUtc
                };
            }
        }

        // MembershipLock 안에서만 호출합니다. 방 생성과 일반 입장이 같은 예약 등록 절차를 사용합니다.
        private RoomAdmission ReserveRoom(UserInfo user, RoomInfo room, bool awaitingJoinRequest = false)
        {
            var admission = new RoomAdmission
            {
                UserId = user.Id,
                RoomId = room.Id,
                Token = NewToken(),
                AwaitingJoinRequest = awaitingJoinRequest,
                ExpiresAtUtc = Now.AddSeconds(roomOptions.ServerReadyTimeoutSeconds + 60)
            };
            reservations.Add(user.Id, admission);
            room.ReservedUsers++;
            user.PendingRoomId = room.Id;
            return admission;
        }

        // 실행 시 전달한 서버 비밀값과 현재 SignalR 연결을 확인합니다. 다른 연결의 중복 등록은 거절합니다.
        public bool RegisterServer(string roomId, string serverToken, string connectionId)
        {
            lock (userService.MembershipLock)
            {
                var room = FindById(roomId);
                if (room == null ||
                    string.IsNullOrEmpty(room.ServerToken) ||
                    room.ServerToken != serverToken ||
                    (room.State != ServerState.Starting &&
                    room.State != ServerState.Running))
                {
                    return false;
                }

                if (serverConnections.TryGetValue(roomId, out var existing) && existing != connectionId)
                {
                    return false;
                }

                serverConnections[roomId] = connectionId;
                OnReady(roomId);
                return true;
            }
        }

        // 등록된 Unity 서버의 접속 보고만 받습니다. 예약을 실제 인원으로 전환하고 자리 번호와 방장을 결정합니다.
        public BaseResponse ConfirmConnection(string roomId, string connectionId, string userId, string admissionToken)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                if (!IsRegisteredServer(roomId, connectionId))
                {
                    return Result(false, "등록된 방 서버만 확인할 수 있습니다.");
                }

                var room = FindById(roomId);
                var user = userService.FindById(userId);
                if (room == null || user == null || room.State != ServerState.Running)
                {
                    return Result(false, "방 서버가 준비되지 않았습니다.");
                }

                // 동일 토큰 재전송은 성공으로 응답하되 재입장은 수행하지 않습니다.
                if (user.CurrentRoomId == roomId && connectedTokens.GetValueOrDefault(userId) == admissionToken)
                {
                    return Participant(user, "이미 접속이 확인되었습니다.");
                }

                if (room.IsEntryLocked)
                {
                    return Result(false, "방장이 새 참가자의 입장을 잠갔습니다.");
                }

                if (room.IsRunning)
                {
                    return Result(false, "진행 중인 방에는 입장할 수 없습니다.");
                }

                if (!reservations.TryGetValue(userId, out var admission) ||
                    admission.RoomId != roomId ||
                    admission.Token != admissionToken)
                {
                    return Result(false, "유효한 입장 예약이 없습니다.");
                }

                if (user.CurrentLobbyId != room.LobbyId || user.CurrentRoomId.Length != 0)
                {
                    return Result(false, "사용자 상태가 변경되었습니다.");
                }

                // 예약 검사가 끝나면 실제 참가자 목록에 추가합니다. 이후 예약을 해제하고 현재 소속을 갱신합니다.
                if (!room.Users.TryAdd(userId, user))
                {
                    return Result(false, "사용자 등록에 실패했습니다.");
                }

                RemoveReservation(admission);
                user.CurrentRoomId = room.Id;
                user.CurrentRoomName = room.Name;
                // 0부터 정원까지의 번호 중 다른 참가자가 쓰지 않는 첫 자리를 배정합니다.
                user.RoomIndex = Enumerable.Range(0, room.MaxUsers).First(index => !room.Users.Values.Any(other => other.Id != userId && other.RoomIndex == index));
                connectedTokens[userId] = admissionToken;
                // 초기 방장이 접속 예약 중이면 다른 참가자가 먼저 연결되어도 방장 자리를 유지합니다.
                bool masterIsJoining = reservations.TryGetValue(room.MasterUserId, out var masterAdmission) &&
                    masterAdmission.RoomId == room.Id;
                if (!room.Users.ContainsKey(room.MasterUserId) && !masterIsJoining)
                {
                    room.MasterUserId = userId;
                }

                room.LastActiveTime = Now;
                return Participant(user, "방 접속이 확인되었습니다.");
            }
        }

        private static RoomParticipantResponse Participant(UserInfo user, string message)
        {
            return new()
            {
                Success = true,
                Message = message,
                UserId = user.Id,
                UserName = user.Name,
                RoomIndex = user.RoomIndex
            };
        }

        // Netcode ConnectionApproval에서 조회합니다. 실제 접속 완료는 ConfirmConnection으로 확정합니다.
        public BaseResponse ValidateConnection(string roomId, string connectionId, string userId, string admissionToken)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var user = userService.FindById(userId);
                var room = FindById(roomId);
                bool valid = IsRegisteredServer(roomId, connectionId) &&
                    room?.State == ServerState.Running &&
                    !room.IsEntryLocked &&
                    !room.IsRunning &&
                    user != null &&
                    user.CurrentLobbyId == room.LobbyId &&
                    user.CurrentRoomId.Length == 0 &&
                    reservations.TryGetValue(userId, out var admission) &&
                    admission.RoomId == roomId &&
                    admission.Token == admissionToken;
                return Result(valid, valid ? "유효한 예약입니다." : "유효한 입장 예약이 없습니다.");
            }
        }

        // 예전 연결의 늦은 종료 보고가 새 접속을 지우지 않도록 현재 입장 토큰과 일치하는지 확인합니다.
        public BaseResponse ClientDisconnected(string roomId, string connectionId, string userId, string admissionToken)
        {
            lock (userService.MembershipLock)
            {
                if (!IsRegisteredServer(roomId, connectionId))
                {
                    return Result(false, "등록된 방 서버만 알릴 수 있습니다.");
                }

                if (reservations.TryGetValue(userId, out var pending) &&
                    pending.RoomId == roomId &&
                    pending.Token == admissionToken)
                {
                    RemoveReservation(pending);
                    return Result(true, "예약이 해제되었습니다.");
                }

                var room = FindById(roomId);
                if (room == null || connectedTokens.GetValueOrDefault(userId) != admissionToken)
                {
                    return Result(false, "현재 접속과 일치하지 않습니다.");
                }

                return LeaveRoom(userId, new RoomMembershipRequest { LobbyId = room.LobbyId, RoomId = roomId });
            }
        }

        // 방 ID에 등록된 SignalR 연결과 현재 호출 연결이 같은지 확인합니다.
        private bool IsRegisteredServer(string roomId, string connectionId)
        {
            return serverConnections.TryGetValue(roomId, out var registered) && registered == connectionId;
        }

        // 현재 등록된 Unity 서버 연결이 끊어진 경우에만 방 상태를 초기화합니다.
        public void ServerDisconnected(string roomId, string connectionId)
        {
            lock (userService.MembershipLock)
            {
                if (!IsRegisteredServer(roomId, connectionId))
                {
                    return;
                }

                var room = FindById(roomId);
                if (room != null)
                {
                    ResetRoomServer(room, "SignalR 연결 종료");
                }
            }
        }

        // 실제 접속으로 이어지지 않은 예약을 만료시켜 자리와 사용자 대기 상태를 돌려놓습니다.
        public void ExpireReservations()
        {
            lock (userService.MembershipLock)
            {
                foreach (var admission in reservations.Values.Where(x => x.ExpiresAtUtc <= Now).ToArray())
                {
                    RemoveReservation(admission);
                }
            }
        }

        // 예약 저장소·예약 인원·사용자 대기 상태를 함께 정리하고 서버가 더 필요한지도 판단합니다.
        private void RemoveReservation(RoomAdmission admission)
        {
            reservations.Remove(admission.UserId);
            var room = FindById(admission.RoomId);
            if (room != null)
            {
                room.ReservedUsers = Math.Max(0, room.ReservedUsers - 1);
            }

            var user = userService.FindById(admission.UserId);
            if (user != null && user.PendingRoomId == admission.RoomId)
            {
                user.PendingRoomId = string.Empty;
            }

            if (room != null)
            {
                // 예약 중이던 초기 방장이 취소·만료되면 이미 접속한 참가자에게 권한을 넘깁니다.
                // 접속 확인에서는 참가자 추가 후 이 함수를 호출하므로 정상 입장한 방장은 유지됩니다.
                if (room.MasterUserId == admission.UserId && !room.Users.ContainsKey(admission.UserId) && !room.Users.IsEmpty)
                {
                    ChangeMaster(room);
                }

                RemoveEmptyRoom(room);
            }
        }

        // MembershipLock 안에서 호출합니다. 이미 삭제된 방은 무시하고, 참가자·예약이 없는 현재 방만 삭제합니다.
        private void RemoveEmptyRoom(RoomInfo room)
        {
            if (FindById(room.Id) == room && room.Users.IsEmpty && room.ReservedUsers == 0)
            {
                DeleteRoom(lobbyService.FindById(room.LobbyId), room);
            }
        }

        private static BaseResponse Result(bool success, string message)
        {
            return new()
            {
                Success = success,
                Message = message
            };
        }
    }
}
