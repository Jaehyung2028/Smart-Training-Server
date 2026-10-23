using Smart_Training_Server.Helper;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;

// 방 서버의 시작·준비·종료·비정상 종료 및 로비 일괄 종료를 관리합니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 이미 준비 중인 서버는 기존 대기를 공유하고, 필요한 경우에만 새로 시작합니다.
        private async Task<bool> EnsureRoomServer(RoomInfo room)
        {
            TaskCompletionSource<bool> readyTask;
            lock (userService.MembershipLock)
            {
                if (FindById(room.Id) != room || room.ReservedUsers == 0)
                {
                    return false;
                }

                if (room.State == ServerState.Running)
                {
                    return true;
                }

                readyTask = readyMap.GetOrAdd(
                    room.Id,
                    _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                );
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
                        logger.LogError(ex, "방 서버 초기화 실패: {RoomId}", room.Id);
                        ResetRoomServer(room, "초기화 실패");
                        return false;
                    }
                }
            }

            return await WaitRoomReady(room, readyTask);
        }

        // 서버 실행 전에 안내 주소와 서버 등록용 비밀값을 준비합니다.
        private void InitializeRoom(RoomInfo room)
        {
            // 클라이언트가 사용할 주소를 준비합니다. 포트는 실제 서버 실행 시 배정받습니다.
            room.IpAddress = string.IsNullOrEmpty(roomOptions.AdvertisedAddress) ? NetworkHelper.GetLocalIPAddress() : roomOptions.AdvertisedAddress;
            room.ServerToken = NewToken();
            room.LastActiveTime = DateTime.UtcNow;
        }

        // 운영 기록을 남기고 전용 프로세스를 실행합니다. 실행됐다고 아직 접속 준비가 끝난 것은 아닙니다.
        private bool LaunchRoomServer(RoomInfo room)
        {
            operations.Record("room", room.Id, room.LobbyId, room.Name, "starting");
            try
            {
                room.Port = processManager.Launch(room.Id, roomOptions.ServerExecutablePath, room.ServerToken);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "방 서버 실행 실패: {RoomId}", room.Id);
                return false;
            }
        }

        // Unity 서버의 등록 신호를 기다립니다. 제한 시간 내 준비되지 않으면 실패로 처리합니다.
        private async Task<bool> WaitRoomReady(RoomInfo room, TaskCompletionSource<bool> readyTask)
        {
            try
            {
                return await readyTask.Task.WaitAsync(TimeSpan.FromSeconds(roomOptions.ServerReadyTimeoutSeconds));
            }
            catch (OperationCanceledException)
            {
                // 방이 삭제된 경우입니다. 공유 준비 작업에 사용자별 취소 신호를 전달하지 않습니다.
                return false;
            }
            catch (TimeoutException)
            {
                // 아래에서 현재 준비 작업인지 다시 확인한 뒤 시간 초과를 정리합니다.
            }

            lock (userService.MembershipLock)
            {
                if (room.State == ServerState.Running)
                {
                    return true;
                }

                if (readyMap.TryGetValue(room.Id, out var current) && current == readyTask)
                {
                    ResetRoomServer(room, "준비 대기 시간 초과");
                }
            }

            logger.LogWarning("방 서버 준비 시간 초과: {RoomId}", room.Id);
            return false;
        }

        // Unity 서버 등록이 성공하면 프로세스 상태를 Running으로 바꾸고 기다리던 요청을 깨웁니다.
        public void OnReady(string roomId)
        {
            RoomInfo? room = FindById(roomId);
            if (room == null)
            {
                return;
            }

            room.State = ServerState.Running;
            operations.Record("room", room.Id, room.LobbyId, room.Name, "ready");
            room.LastActiveTime = DateTime.UtcNow;
            if (readyMap.TryRemove(roomId, out var readyTask))
            {
                readyTask.TrySetResult(true);
            }
        }

        // Unity 프로세스가 예기치 않게 종료된 경우 ASP.NET에 남은 예약·참가자 상태도 함께 정리합니다.
        public void CheckServerProcesses()
        {
            lock (userService.MembershipLock)
            {
                foreach (var room in rooms.Values.ToArray())
                {
                    if (room.State is not (ServerState.Starting or ServerState.Running))
                    {
                        continue;
                    }

                    if (!processManager.IsRunning(room.Id))
                    {
                        ResetRoomServer(room, "Unity 프로세스가 종료되었습니다.");
                    }
                }
            }
        }

        // MembershipLock 안에서 호출합니다. 실행 실패·연결 종료 시 예약·참가자·준비 대기를 함께 정리합니다.
        private void ResetRoomServer(RoomInfo room, string reason)
        {
            room.State = ServerState.Creating;
            room.ServerToken = string.Empty;
            serverConnections.Remove(room.Id);
            foreach (var admission in reservations.Values.Where(x => x.RoomId == room.Id).ToArray())
            {
                RemoveReservation(admission);
            }

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
            // 준비를 기다리던 HTTP 요청에도 실패 결과를 전달해 불필요하게 계속 기다리지 않도록 합니다.
            if (readyMap.TryRemove(room.Id, out var waiting))
            {
                waiting.TrySetResult(false);
            }

            StopRoomProcess(room);
            operations.Record(
                "room",
                room.Id,
                room.LobbyId,
                room.Name,
                reason is "입장 취소" or "방장이 방을 닫았습니다." or "로비 종료 예약 실행" ? "stopped" : "failed",
                reason
            );
            RemoveEmptyRoom(room);

            logger.LogInformation("방 서버 상태 초기화: {RoomId}, 사유: {Reason}", room.Id, reason);
        }

        // 방 서비스는 종료 요청과 화면에 안내할 포트 값만 정리합니다.
        // 실제 프로세스·포트의 정리는 실행 도구가 담당하므로 시간 초과를 따로 처리하지 않습니다.
        private void StopRoomProcess(RoomInfo room)
        {
            room.Port = 0;
            processManager.Stop(room.Id);
        }

        // 종료할 로비에 속한 방을 순회하며 서버 상태를 초기화하고 방 정보를 삭제합니다.
        internal void CloseLobbyRooms(string lobbyId)
        {
            lock (userService.MembershipLock)
            {
                foreach (var room in rooms.Values.Where(x => x.LobbyId == lobbyId).ToArray())
                {
                    room.IsClosing = true;
                    ResetRoomServer(room, "로비 종료 예약 실행");
                    DeleteRoom(lobbyService.FindById(lobbyId), room);
                }
            }
        }
    }
}
