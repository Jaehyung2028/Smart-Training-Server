using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;

namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    public void CheckServerProcesses()
    {
        lock (userService.MembershipLock)
        {
            foreach (var room in rooms.Values.ToArray())
            {
                if (room.State is not (ServerState.Starting or ServerState.Running)) continue;
                if (!processManager.IsRunning(room.Id))
                    ResetRoomServer(room, "Unity 프로세스가 종료되었습니다.");
            }
        }
    }

    // 호출자는 MembershipLock을 보유합니다. READY 대기를 해제하고 모든 예약/접속을 정리합니다.
    private void ResetRoomServer(RoomInfo room, string reason)
    {
        room.State = ServerState.Creating;
        room.ServerToken = string.Empty;
        serverConnections.Remove(room.Id);
        foreach (var admission in reservations.Values.Where(x => x.RoomId == room.Id).ToArray())
            RemoveReservation(admission);
        foreach (var user in room.Users.Values)
        {
            user.CurrentRoomId = string.Empty;
            user.CurrentRoomName = string.Empty;
            user.RoomIndex = -1;
            connectedTokens.Remove(user.Id);
        }
        room.Users.Clear();
        room.MasterUserId = string.Empty;
        room.IsEntryLocked = false;
        room.IsRunning = false;
        room.PhaseRevision++;
        if (readyMap.TryRemove(room.Id, out var waiting)) waiting.TrySetResult(false);
        processManager.Stop(room.Id);
        operations.Record("room", room.Id, room.LobbyId, room.Name,
            reason is "입장 취소" or "방장이 방을 닫았습니다." or "로비 종료 예약 실행" ? "stopped" : "failed", reason);
        Console.WriteLine($"[Room] 서버 상태 초기화 ({room.Id}): {reason}");
    }
}
