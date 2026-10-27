using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Responses.Room;

// 방·참가자 조회와 조회 권한을 관리합니다. 운영용 조회는 예약 상태를 변경하지 않습니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // null: 없는 로비, 빈 배열: 존재하지만 방이 없는 로비.
        public IReadOnlyList<RoomSummaryResponse>? GetRoomsByLobby(string lobbyId)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var lobby = lobbyService.FindById(lobbyId);
                return lobby?.Rooms.Values.OrderBy(room => room.CreatedAt).ThenBy(room => room.Id, StringComparer.Ordinal).Select(ToRoomSummary).ToArray();
            }
        }

        // 지정 로비 안의 방인지 확인한 뒤 단일 요약을 반환합니다. 다른 로비의 방은 조회하지 않습니다.
        public RoomSummaryResponse? GetRoomByLobby(string lobbyId, string roomId)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var lobby = lobbyService.FindById(lobbyId);
                var room = lobby == null ? null : FindById(lobby, roomId);
                return room == null ? null : ToRoomSummary(room);
            }
        }

        private static RoomSummaryResponse ToRoomSummary(RoomInfo room)
        {
            return new()
            {
                Id = room.Id,
                LobbyId = room.LobbyId,
                Name = room.Name,
                ContentId = room.Content.RootContent,
                ContentNodeId = room.Content.Id,
                ContentTitle = room.Content.Title,
                ContentType = room.Content.ContentType,
                AssetId = room.Content.AssetId,
                MaxUsers = room.MaxUsers,
                ConnectedUsers = room.Users.Count,
                ReservedUsers = room.ReservedUsers,
                AvailableSlots = Math.Max(0, room.MaxUsers - room.Users.Count - room.ReservedUsers),
                MasterUserId = room.MasterUserId,
                IsEntryLocked = room.IsEntryLocked,
                HasPassword = room.HasPassword,
                IsRunning = room.IsRunning,
                State = room.State,
                CreatedAt = room.CreatedAt

            };
        }

        // HTTP 상태와 참가자 목록을 함께 반환합니다. 로비 존재 여부와 요청자의 소속부터 검사합니다.
        public (int StatusCode, IReadOnlyList<LobbyParticipantResponse> Participants) GetLobbyParticipants(string lobbyId, string authenticatedUserId)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var lobby = lobbyService.FindById(lobbyId);
                if (lobby == null)
                {
                    return (404, []);
                }

                var caller = userService.FindById(authenticatedUserId);
                if (caller == null || caller.CurrentLobbyId != lobbyId || !lobby.Users.ContainsKey(caller.Id))
                {
                    return (403, []);
                }

                var result = new List<LobbyParticipantResponse>();
                foreach (var member in lobby.Users.Values)
                {
                    var user = userService.FindById(member.Id);
                    if (user == null || user.CurrentLobbyId != lobbyId)
                    {
                        continue;
                    }

                    var room = user.CurrentRoomId.Length > 0 ? FindById(user.CurrentRoomId) : null;
                    if (room != null &&
                        room.LobbyId == lobbyId &&
                        !room.IsClosing &&
                        room.Users.ContainsKey(user.Id) &&
                        connectedTokens.ContainsKey(user.Id))
                    {
                        result.Add(new() {
                                UserId = user.Id,
                                Nickname = user.Name,
                                ConnectionState = "connected",
                                RoomId = room.Id,
                                RoomName = room.Name,
                                IsRoomMaster = room.MasterUserId == user.Id,
                                RoomIsRunning = room.IsRunning
                            });
                    }
                    else if (reservations.TryGetValue(user.Id, out var reservation) &&
                        FindById(reservation.RoomId)is { } pending &&
                        pending.LobbyId == lobbyId &&
                        !pending.IsClosing)
                    {
                        result.Add(new() {
                                UserId = user.Id,
                                Nickname = user.Name,
                                ConnectionState = "reserved",
                                RoomId = pending.Id,
                                RoomName = pending.Name
                            });
                    }
                    else
                    {
                        result.Add(new() {
                                UserId = user.Id,
                                Nickname = user.Name
                            });
                    }
                }

                return (200, result.OrderBy(item => item.Nickname, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.UserId, StringComparer.Ordinal).ToArray());
            }
        }

        // 공개 조회와 달리 예약 만료나 상태 변경 없이 운영용 스냅샷만 만듭니다.
        public IReadOnlyList<RoomSummaryResponse> GetOperationsRooms()
        {
            lock (userService.MembershipLock)
            {
                return rooms.Values.OrderBy(x => x.LobbyId).ThenBy(x => x.Id).Select(ToRoomSummary).ToArray();
            }
        }

        // 현재 방 소속·실제 참가자·접속 토큰이 모두 일치하는 경우에만 방 채팅을 허용합니다.
        internal bool CanUseRoomChat(string userId, string roomId, string admissionToken)
        {
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(admissionToken))
                {
                    return false;
                }

                var room = FindById(roomId);
                var user = userService.FindById(userId);
                return room != null && !room.IsClosing && user != null && user.CurrentRoomId == room.Id && user.CurrentLobbyId == room.LobbyId && room.Users.ContainsKey(userId) && serverConnections.ContainsKey(roomId) && connectedTokens.TryGetValue(userId, out var current) && current == admissionToken;
            }
        }
    }
}
