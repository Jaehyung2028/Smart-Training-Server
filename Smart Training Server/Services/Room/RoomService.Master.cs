using Smart_Training_Server.Models;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Room;

// 방장 권한으로 실행하는 위임·강퇴·방 종료를 모읍니다. Unity 서버 상태 조회도 함께 제공합니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 요청자의 방장 권한과 대상자의 실제 접속 상태를 확인한 뒤 현재 방장 ID를 바꿉니다.
        public RoomMasterResponse TransferRoomMaster(string authenticatedUserId, RoomParticipantRequest request)
        {
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrWhiteSpace(request.LobbyId) ||
                    string.IsNullOrWhiteSpace(request.RoomId) ||
                    string.IsNullOrWhiteSpace(request.TargetUserId))
                {
                    return new()
                    {
                        Success = false,
                        Message = "로비, 방, 위임할 참가자를 지정해주세요."

                    };
                }

                var room = FindById(request.RoomId);
                if (room == null || room.LobbyId != request.LobbyId)
                {
                    return new()
                    {
                        Success = false,
                        Message = "해당 로비에서 방을 찾을 수 없습니다."

                    };
                }

                if (!IsConnectedMaster(room, authenticatedUserId))
                {
                    return new()
                    {
                        Success = false,
                        Message = "현재 방장만 방장을 위임할 수 있습니다."

                    };
                }

                var target = userService.FindById(request.TargetUserId);
                if (target == null ||
                    target.CurrentRoomId != room.Id ||
                    !room.Users.ContainsKey(target.Id) ||
                    !connectedTokens.ContainsKey(target.Id))
                {
                    return new()
                    {
                        Success = false,
                        Message = "같은 방에 접속 중인 참가자에게만 위임할 수 있습니다."

                    };
                }

                if (target.Id == authenticatedUserId)
                {
                    return new()
                    {
                        Success = true,
                        MasterUserId = authenticatedUserId,
                        IsEntryLocked = room.IsEntryLocked,
                        Message = "이미 방장입니다."

                    };
                }

                room.MasterUserId = target.Id;
                room.LastActiveTime = Now;
                return new()
                {
                    Success = true,
                    MasterUserId = target.Id,
                    IsEntryLocked = room.IsEntryLocked,
                    Message = "방장을 위임했습니다."

                };
            }
        }

        // 현재 등록된 전용 서버만 방장 정보를 조회할 수 있습니다.
        // Unity 방 서버가 ASP.NET의 최신 방장·잠금·설정·진행 상태를 받아갈 때 사용합니다.
        public RoomMasterResponse GetRoomMaster(string roomId, string connectionId)
        {
            lock (userService.MembershipLock)
            {
                if (!IsRegisteredServer(roomId, connectionId))
                {
                    return new()
                    {
                        Success = false,
                        Message = "등록된 방 서버만 조회할 수 있습니다."

                    };
                }

                var room = FindById(roomId);
                if (room == null)
                {
                    return new()
                    {
                        Success = false,
                        Message = "방을 찾을 수 없습니다."

                    };
                }

                return new()
                {
                    Success = true,
                    MasterUserId = room.MasterUserId,
                    IsEntryLocked = room.IsEntryLocked,
                    RoomName = room.Name,
                    MaxUsers = room.MaxUsers,
                    IsRunning = room.IsRunning,
                    PhaseRevision = room.PhaseRevision

                };
            }
        }

        // 방장 권한을 검사한 뒤 등록된 Unity 서버에 강퇴를 요청합니다. API가 네트워크 연결을 직접 끊지는 않습니다.
        public async Task<BaseResponse> KickRoomUserAsync(string authenticatedUserId, RoomParticipantRequest request, IHubContext<RoomHub> roomHub)
        {
            // 잠금 안에서 대상 연결과 토큰을 확정하고, 실제 네트워크 전송은 잠금 밖에서 수행합니다.
            RoomDisconnectNotice notice;
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrWhiteSpace(request.LobbyId) ||
                    string.IsNullOrWhiteSpace(request.RoomId) ||
                    string.IsNullOrWhiteSpace(request.TargetUserId))
                {
                    return Result(false, "로비, 방, 강퇴할 참가자를 지정해주세요.");
                }

                var room = FindById(request.RoomId);
                if (room == null || room.LobbyId != request.LobbyId)
                {
                    return Result(false, "해당 로비에서 방을 찾을 수 없습니다.");
                }

                if (!IsConnectedMaster(room, authenticatedUserId))
                {
                    return Result(false, "현재 방장만 참가자를 강퇴할 수 있습니다.");
                }

                if (request.TargetUserId == authenticatedUserId)
                {
                    return Result(false, "자신은 강퇴할 수 없습니다. 방에서 나가기를 이용해주세요.");
                }

                var target = userService.FindById(request.TargetUserId);
                if (target == null ||
                    target.CurrentRoomId != room.Id ||
                    !room.Users.ContainsKey(target.Id) ||
                    !connectedTokens.TryGetValue(target.Id, out var token))
                {
                    return Result(false, "같은 방에 접속 중인 참가자만 강퇴할 수 있습니다.");
                }

                if (!serverConnections.TryGetValue(room.Id, out var connection))
                {
                    return Result(false, "방 서버 연결을 확인할 수 없습니다.");
                }

                notice = new(connection, target.Id, token);
            }

            // 실제 종료 보고 전까지 자리를 유지합니다. 입장 토큰도 보내 오래된 강퇴 요청이 새 접속을 끊지 않게 합니다.
            try
            {
                await roomHub.Clients.Client(notice.ConnectionId).SendAsync("KickUser", notice.UserId, notice.AdmissionToken).WaitAsync(TimeSpan.FromSeconds(5));
                return Result(true, "강퇴 요청을 전달했습니다.");
            }
            catch (Exception)
            {
                return Result(false, "강퇴 요청을 전달하지 못했습니다. 접속 상태를 확인한 뒤 다시 시도해주세요.");
            }
        }

        // 방을 종료 중으로 표시하고 예약·소속을 해제한 뒤 Unity 서버에 종료를 알립니다. 실패해도 finally에서 정리합니다.
        public async Task<BaseResponse> CloseRoomAsync(string userId, RoomMembershipRequest request, IHubContext<RoomHub> hub)
        {
            Models.RoomInfo room;
            string? connection;
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrWhiteSpace(request.RoomId) || string.IsNullOrWhiteSpace(request.LobbyId))
                {
                    return Result(false, "로비와 방을 지정해주세요.");
                }

                var found = FindById(request.RoomId);
                if (found == null || found.LobbyId != request.LobbyId)
                {
                    return Result(false, "방을 찾을 수 없습니다.");
                }

                if (found.IsClosing || !IsConnectedMaster(found, userId))
                {
                    return Result(false, "현재 접속한 방장만 방을 닫을 수 있습니다.");
                }

                // 종료 알림을 보내는 동안 다른 요청이 새로 들어오지 못하도록 종료 상태와 입장 잠금을 먼저 설정합니다.
                room = found;
                room.IsClosing = true;
                room.IsEntryLocked = true;
                room.State = ServerState.Stopping;
                connection = serverConnections.GetValueOrDefault(room.Id);
                foreach (var reservation in reservations.Values.Where(x => x.RoomId == room.Id).ToArray())
                {
                    RemoveReservation(reservation);
                }

                foreach (var user in room.Users.Values)
                {
                    user.CurrentRoomId = "";
                    user.CurrentRoomName = "";
                    user.RoomIndex = -1;
                    connectedTokens.Remove(user.Id);
                }

                room.Users.Clear();
            }

            try
            {
                if (connection != null)
                {
                    await hub.Clients.Client(connection).SendAsync("CloseRoom", room.Id).WaitAsync(TimeSpan.FromSeconds(3));
                    // 전용 서버가 연결 종료 사유를 전달할 시간을 확보합니다.
                    await Task.Delay(750);
                }
            }
            catch (Exception)
            { /* 전용 서버와 통신할 수 없어도 정리는 계속합니다. */
            }
            // 통신 실패와 무관하게 반드시 실행됩니다. 프로세스와 방 저장소가 남지 않도록 마지막 정리를 수행합니다.
            finally
            {
                lock (userService.MembershipLock)
                {
                    ResetRoomServer(room, "방장이 방을 닫았습니다.");
                    DeleteRoom(lobbyService.FindById(room.LobbyId), room);
                }
            }

            return Result(true, "방을 닫았습니다. 로비로 돌아왔습니다.");
        }

        // 방장 동작에 공통인 현재 접속·소속·방장 권한을 한곳에서 확인합니다.
        // 호출자는 MembershipLock을 보유합니다. 종료·진행 상태 등 설정별 제한은 각 함수에서 검사합니다.
        private bool IsConnectedMaster(RoomInfo room, string userId)
        {
            var user = string.IsNullOrWhiteSpace(userId) ? null : userService.FindById(userId);
            return user != null && user.CurrentRoomId == room.Id && room.Users.ContainsKey(userId) && connectedTokens.ContainsKey(userId) && room.MasterUserId == userId;
        }
    }
}
