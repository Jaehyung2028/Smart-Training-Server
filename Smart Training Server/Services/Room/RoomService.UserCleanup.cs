// 로그아웃·세션 만료 사용자의 예약과 방 소속을 정리하고 Unity 서버로 보낼 종료 통지 목록을 반환합니다.
namespace Smart_Training_Server.Services.Room
{
    // 잠금 안에서 정한 해제 통지 대상입니다. 연결 ID·사용자·입장 토큰만 수집한 뒤 실제 전송은 잠금 밖에서 합니다.
    internal sealed record RoomDisconnectNotice(string ConnectionId, string UserId, string AdmissionToken);
    public partial class RoomService
    {
        // 사용자의 예약·실제 소속을 제거하고 방장 위임·빈 방 삭제를 처리합니다. 네트워크 통지는 반환된 목록을 통해 잠금 밖에서 합니다.
        internal IReadOnlyList<RoomDisconnectNotice> RemoveUserFromRooms(string userId)
        {
            lock (userService.MembershipLock)
            {
                var notices = new List<RoomDisconnectNotice>();
                string? pendingRoomId = null;
                if (reservations.TryGetValue(userId, out var pending))
                {
                    pendingRoomId = pending.RoomId;
                    if (serverConnections.TryGetValue(pending.RoomId, out var pendingConnection))
                    {
                        notices.Add(new(pendingConnection, userId, pending.Token));
                    }

                    RemoveReservation(pending);
                }

                foreach (var room in rooms.Values.ToArray())
                {
                    bool wasMaster = room.MasterUserId == userId;
                    bool removed = room.Users.TryRemove(userId, out _);
                    if (!removed && !wasMaster && room.Id != pendingRoomId)
                    {
                        continue;
                    }

                    if (removed &&
                        serverConnections.TryGetValue(room.Id, out var connection) &&
                        connectedTokens.TryGetValue(userId, out var token))
                    {
                        notices.Add(new(connection, userId, token));
                    }

                    room.LastActiveTime = Now;
                    var lobby = lobbyService.FindById(room.LobbyId);
                    if (lobby != null)
                    {
                        lobby.LastActiveTime = Now;
                    }

                    if (room.Users.IsEmpty && room.ReservedUsers == 0)
                    {
                        DeleteRoom(lobby, room);
                    }
                    else if (wasMaster)
                    {
                        ChangeMaster(room);
                    }
                }

                connectedTokens.Remove(userId);
                var user = userService.FindById(userId);
                if (user != null)
                {
                    user.PendingRoomId = string.Empty;
                    user.CurrentRoomId = string.Empty;
                    user.CurrentRoomName = string.Empty;
                    user.RoomIndex = -1;
                }

                return notices;
            }
        }
    }
}
