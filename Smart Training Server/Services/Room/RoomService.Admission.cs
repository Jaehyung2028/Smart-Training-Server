using System.Security.Cryptography;
using Smart_Training_Server.Models;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    private readonly Dictionary<string, RoomAdmission> reservations = new();
    private readonly Dictionary<string, string> connectedTokens = new();
    private readonly Dictionary<string, string> serverConnections = new();
    private readonly TimeProvider timeProvider;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static JoinRoomResponse JoinError(string message) => new() { Success = false, Message = message };

    public async Task<JoinRoomResponse> JoinRoom(JoinRoomRequest request, CancellationToken cancellationToken = default)
    {
        RoomAdmission admission;
        RoomInfo room;
        lock (userService.MembershipLock)
        {
            if (cancellationToken.IsCancellationRequested) return JoinError("입장을 취소했습니다.");
            ExpireReservations();
            var user = userService.FindById(request.UserId);
            var lobby = lobbyService.FindById(request.LobbyId);
            var found = lobby == null ? null : FindById(lobby, request.RoomId);
            if (user == null || lobby == null || found == null)
                return JoinError("사용자, 로비 또는 방을 찾을 수 없습니다.");
            room = found;
            if (room.IsClosing) return JoinError("방장이 방을 닫고 있습니다.");
            if (!RoomPassword.Matches(room, request.Password)) return JoinError("방 비밀번호가 올바르지 않습니다.");
            if (user.CurrentLobbyId != lobby.Id) return JoinError("해당 로비에 먼저 입장해주세요.");
            if (room.IsRunning) return JoinError("진행 중인 방에는 입장할 수 없습니다.");
            if (room.IsEntryLocked) return JoinError("방장이 새 참가자의 입장을 잠갔습니다.");
            if (user.CurrentRoomId.Length != 0 || user.PendingRoomId.Length != 0)
                return JoinError("이미 방에 입장했거나 입장을 예약했습니다.");
            if (room.Users.Count + room.ReservedUsers >= room.MaxUsers)
                return JoinError("방이 가득 찼습니다.");
            admission = new RoomAdmission
            {
                UserId = user.Id, RoomId = room.Id, Token = NewToken(),
                // 설정된 서버 시작 대기보다 충분히 긴 내부 예약 기한입니다.
                ExpiresAtUtc = Now.AddSeconds(roomOptions.ServerReadyTimeoutSeconds + 60)
            };
            reservations.Add(user.Id, admission);
            room.ReservedUsers++;
            user.PendingRoomId = room.Id;
        }

        bool ready;
        try { ready = await EnsureRoomServer(room).WaitAsync(cancellationToken); }
        catch { ready = false; }
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            if (!reservations.TryGetValue(admission.UserId, out var current) || current != admission)
            {
                StopUnusedServer(room);
                return JoinError("입장 예약이 취소되거나 만료되었습니다.");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                RemoveReservation(admission);
                // A shared launch must continue for other reservations. Only stop an unused launch.
                if (room.State == ServerState.Starting && room.Users.IsEmpty && room.ReservedUsers == 0)
                    ResetRoomServer(room, "입장 취소");
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
                Success = true, Message = "입장이 예약되었습니다. Unity 서버에 접속해주세요.",
                Room = room, AdmissionToken = admission.Token, ExpiresAtUtc = admission.ExpiresAtUtc
            };
        }
    }

    public bool RegisterServer(string roomId, string serverToken, string connectionId)
    {
        lock (userService.MembershipLock)
        {
            var room = FindById(roomId);
            if (room == null || string.IsNullOrEmpty(room.ServerToken) || room.ServerToken != serverToken ||
                (room.State != ServerState.Starting && room.State != ServerState.Running)) return false;
            if (serverConnections.TryGetValue(roomId, out var existing) && existing != connectionId) return false;
            serverConnections[roomId] = connectionId;
            OnReady(roomId);
            return true;
        }
    }

    public BaseResponse ConfirmConnection(string roomId, string connectionId, string userId, string admissionToken)
    {
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            if (!IsRegisteredServer(roomId, connectionId)) return Result(false, "등록된 방 서버만 확인할 수 있습니다.");
            var room = FindById(roomId);
            var user = userService.FindById(userId);
            if (room == null || user == null || room.State != ServerState.Running) return Result(false, "방 서버가 준비되지 않았습니다.");
            // 동일 토큰 재전송은 성공으로 응답하되 재입장은 수행하지 않습니다.
            if (user.CurrentRoomId == roomId && connectedTokens.GetValueOrDefault(userId) == admissionToken)
                return Participant(user, "이미 접속이 확인되었습니다.");
            if (room.IsEntryLocked) return Result(false, "방장이 새 참가자의 입장을 잠갔습니다.");
            if (room.IsRunning) return Result(false, "진행 중인 방에는 입장할 수 없습니다.");
            if (!reservations.TryGetValue(userId, out var admission) || admission.RoomId != roomId || admission.Token != admissionToken)
                return Result(false, "유효한 입장 예약이 없습니다.");
            if (user.CurrentLobbyId != room.LobbyId || user.CurrentRoomId.Length != 0)
                return Result(false, "사용자 상태가 변경되었습니다.");
            if (!room.Users.TryAdd(userId, user)) return Result(false, "사용자 등록에 실패했습니다.");
            RemoveReservation(admission);
            user.CurrentRoomId = room.Id;
            user.CurrentRoomName = room.Name;
            user.RoomIndex = Enumerable.Range(0, room.MaxUsers)
                .First(index => !room.Users.Values.Any(other => other.Id != userId && other.RoomIndex == index));
            connectedTokens[userId] = admissionToken;
            if (!room.Users.ContainsKey(room.MasterUserId)) room.MasterUserId = userId;
            room.LastActiveTime = Now;
            return Participant(user, "방 접속이 확인되었습니다.");
        }
    }

    private static RoomParticipantResponse Participant(UserInfo user, string message) => new()
    {
        Success = true, Message = message, UserId = user.Id, UserName = user.Name, RoomIndex = user.RoomIndex
    };

    // Netcode ConnectionApproval에서 조회합니다. 실제 접속 완료는 ConfirmConnection으로 확정합니다.
    public BaseResponse ValidateConnection(string roomId, string connectionId, string userId, string admissionToken)
    {
        lock (userService.MembershipLock)
        {
            ExpireReservations();
            var user = userService.FindById(userId);
            var room = FindById(roomId);
            bool valid = IsRegisteredServer(roomId, connectionId) && room?.State == ServerState.Running && !room.IsEntryLocked && !room.IsRunning &&
                user != null && user.CurrentLobbyId == room.LobbyId && user.CurrentRoomId.Length == 0 &&
                reservations.TryGetValue(userId, out var admission) && admission.RoomId == roomId && admission.Token == admissionToken;
            return Result(valid, valid ? "유효한 예약입니다." : "유효한 입장 예약이 없습니다.");
        }
    }

    public BaseResponse ClientDisconnected(string roomId, string connectionId, string userId, string admissionToken)
    {
        lock (userService.MembershipLock)
        {
            if (!IsRegisteredServer(roomId, connectionId)) return Result(false, "등록된 방 서버만 알릴 수 있습니다.");
            if (reservations.TryGetValue(userId, out var pending) && pending.RoomId == roomId && pending.Token == admissionToken)
            {
                RemoveReservation(pending);
                return Result(true, "예약이 해제되었습니다.");
            }
            var room = FindById(roomId);
            if (room == null || connectedTokens.GetValueOrDefault(userId) != admissionToken)
                return Result(false, "현재 접속과 일치하지 않습니다.");
            return LeaveRoom(new LeaveRoomRequest { UserId = userId, LobbyId = room.LobbyId, RoomId = roomId });
        }
    }

    private bool IsRegisteredServer(string roomId, string connectionId) =>
        serverConnections.TryGetValue(roomId, out var registered) && registered == connectionId;

    public void ServerDisconnected(string roomId, string connectionId)
    {
        lock (userService.MembershipLock)
        {
            if (!IsRegisteredServer(roomId, connectionId)) return;
            var room = FindById(roomId);
            if (room != null) ResetRoomServer(room, "SignalR 연결 종료");
        }
    }

    public void ExpireReservations()
    {
        lock (userService.MembershipLock)
        {
            foreach (var admission in reservations.Values.Where(x => x.ExpiresAtUtc <= Now).ToArray())
                RemoveReservation(admission);
        }
    }

    private void RemoveReservation(RoomAdmission admission)
    {
        reservations.Remove(admission.UserId);
        var room = FindById(admission.RoomId);
        if (room != null) room.ReservedUsers = Math.Max(0, room.ReservedUsers - 1);
        var user = userService.FindById(admission.UserId);
        if (user != null && user.PendingRoomId == admission.RoomId) user.PendingRoomId = string.Empty;
        if (room != null) StopUnusedServer(room);
    }

    private void StopUnusedServer(RoomInfo room)
    {
        if (room.Users.IsEmpty && room.ReservedUsers == 0 && room.State == ServerState.Running)
        {
            serverConnections.Remove(room.Id);
            room.ServerToken = string.Empty;
            processManager.Stop(room.Id);
            operations.Record("room", room.Id, room.LobbyId, room.Name, "stopped", "접속·예약 인원 없음");
            room.State = ServerState.Creating;
        }
    }

    private static BaseResponse Result(bool success, string message) => new() { Success = success, Message = message };
}
