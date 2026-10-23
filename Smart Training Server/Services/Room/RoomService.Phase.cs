using System.Text.Json;
using Smart_Training_Server.Responses.Room;

// Unity 서버의 방 시작·대기 요청을 권한·버전·참가자 목록과 대조해 반영합니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 전용 서버는 준비 상태를, API는 방장 권한과 정확한 참가자 명단을 검증합니다.
        // Unity 전용 서버가 준비 상태를 확인한 뒤 보내는 진행 상태 요청입니다. API는 방장 권한·버전·참가자 목록을 재확인합니다.
        public RoomMasterResponse SetRoomPhase(
            string roomId,
            string connectionId,
            string userId,
            string token,
            bool running,
            string rosterJson,
            long expectedRevision
        )
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var room = FindById(roomId);
                if (room == null ||
                    room.State != Smart_Training_Server.Enum.ServerState.Running ||
                    !IsRegisteredServer(roomId, connectionId) ||
                    !IsConnectedMaster(room, userId) ||
                    string.IsNullOrEmpty(token) ||
                    connectedTokens.GetValueOrDefault(userId) != token)
                {
                    return new()
                    {
                        Success = false,
                        Message = "현재 방장과 등록된 방 서버만 진행 상태를 변경할 수 있습니다."
                    };
                }

                // 오래된 화면 상태로 보낸 시작·대기 요청이 최신 상태를 덮어쓰지 않도록 버전을 비교합니다.
                if (expectedRevision != room.PhaseRevision)
                {
                    return new()
                    {
                        Success = false,
                        Message = "방 상태가 변경되었습니다. 최신 상태를 확인하고 다시 시도해주세요."
                    };
                }

                if (running && !room.IsRunning)
                {
                    string[]? roster;
                    if (string.IsNullOrWhiteSpace(rosterJson) || rosterJson.Length > 65536)
                    {
                        return new()
                        {
                            Success = false,
                            Message = "참가자 정보가 올바르지 않습니다."
                        };
                    }

                    try
                    {
                        roster = JsonSerializer.Deserialize<string[]>(rosterJson);
                    }
                    catch (JsonException)
                    {
                        return new()
                        {
                            Success = false,
                            Message = "참가자 정보를 확인하지 못했습니다."
                        };
                    }

                    // 서버가 확인한 참가자 목록과 ASP.NET 목록이 정확히 같아야 시작합니다. 아직 연결 중인 예약자가 있어도 거절합니다.
                    if (room.ReservedUsers != 0 ||
                        roster == null ||
                        roster.Length != room.Users.Count ||
                        roster.Distinct(StringComparer.Ordinal).Count() != roster.Length ||
                        roster.Any(id => id == null ||
                        !room.Users.ContainsKey(id)))
                    {
                        return new()
                        {
                            Success = false,
                            Message = "입장 예약 또는 참가자 변경이 있습니다. 접속 완료 후 다시 시작해주세요."
                        };
                    }
                }

                if (room.IsRunning != running)
                {
                    room.IsRunning = running;
                    room.PhaseRevision++;
                }

                room.LastActiveTime = Now;
                return new()
                {
                    Success = true,
                    Message = running ? "방을 시작했습니다." : "대기 상태로 돌아왔습니다.",
                    MasterUserId = room.MasterUserId,
                    IsEntryLocked = room.IsEntryLocked,
                    RoomName = room.Name,
                    MaxUsers = room.MaxUsers,
                    IsRunning = room.IsRunning,
                    PhaseRevision = room.PhaseRevision
                };
            }
        }
    }
}
