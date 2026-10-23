using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;

// 방 이름·정원·입장 잠금·비밀번호 변경을 한곳에서 관리합니다. 각 설정의 세부 제한은 해당 함수에서 확인합니다.
namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 인증된 현재 방장만 이름·정원을 바꿀 수 있습니다. 조건 검사 후 한 번에 상태를 반영합니다.
        public RoomMasterResponse UpdateRoomSettings(string authenticatedUserId, UpdateRoomSettingsRequest request)
        {
            lock (userService.MembershipLock)
            {
                string name = request.RoomName?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(request.LobbyId) ||
                    string.IsNullOrWhiteSpace(request.RoomId) ||
                    name.Length < roomOptions.NameMinLength ||
                    name.Length > roomOptions.NameMaxLength ||
                    string.IsNullOrWhiteSpace(name) ||
                    request.MaxUsers == null ||
                    request.MaxUsers < roomOptions.MinUsers ||
                    request.MaxUsers > roomOptions.MaxUsers)
                {
                    return new()
                    {
                        Success = false,
                        Message = $"방 이름은 {roomOptions.NameMinLength}~{roomOptions.NameMaxLength}자, 정원은 {roomOptions.MinUsers}~{roomOptions.MaxUsers}명으로 지정해주세요."
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
                        Message = "현재 방장만 방 설정을 변경할 수 있습니다."
                    };
                }

                ExpireReservations();
                if (room.IsRunning)
                {
                    return new()
                    {
                        Success = false,
                        Message = "진행 중에는 방 설정을 변경할 수 없습니다."
                    };
                }

                // 현재 연결된 사람뿐 아니라 접속 준비 중인 예약자의 자리도 보장해야 합니다.
                if (request.MaxUsers < room.Users.Count + room.ReservedUsers)
                {
                    return new()
                    {
                        Success = false,
                        Message = "현재 접속·예약 인원보다 정원을 줄일 수 없습니다."
                    };
                }

                var lobby = lobbyService.FindById(room.LobbyId);
                if (lobby == null ||
                    (!roomOptions.AllowDuplicateName &&
                    lobby.Rooms.Values.Any(value => value.Id != room.Id &&
                    value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                {
                    return new()
                    {
                        Success = false,
                        Message = "같은 이름의 방이 이미 존재하거나 로비가 종료되었습니다."
                    };
                }

                room.Name = name;
                room.MaxUsers = request.MaxUsers.Value;
                // 방 이름을 참가자 정보에도 보관하므로 이 복사본까지 갱신해 화면 간 이름이 어긋나지 않게 합니다.
                foreach (var participant in room.Users.Values)
                {
                    participant.CurrentRoomName = name;
                }

                room.LastActiveTime = Now;
                return new()
                {
                    Success = true,
                    Message = "방 설정을 변경했습니다.",
                    MasterUserId = room.MasterUserId,
                    IsEntryLocked = room.IsEntryLocked,
                    RoomName = name,
                    MaxUsers = room.MaxUsers
                };
            }
        }

        // 방장 요청으로 새 참가자 입장을 잠그거나 해제합니다. 기존 접속자를 강퇴하는 기능은 아닙니다.
        public RoomMasterResponse SetRoomEntryLock(string authenticatedUserId, SetRoomEntryLockRequest request)
        {
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrWhiteSpace(request.LobbyId) ||
                    string.IsNullOrWhiteSpace(request.RoomId) ||
                    request.IsEntryLocked == null)
                {
                    return new()
                    {
                        Success = false,
                        Message = "로비, 방, 입장 잠금 상태를 지정해주세요."
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
                        Message = "현재 방장만 입장 잠금을 변경할 수 있습니다."
                    };
                }

                room.IsEntryLocked = request.IsEntryLocked.Value;
                // 잠금 전에 만들어진 예약도 취소해, 이미 예약한 사용자가 뒤늦게 입장하는 것을 막습니다.
                if (room.IsEntryLocked)
                {
                    foreach (var admission in reservations.Values.Where(value => value.RoomId == room.Id).ToArray())
                    {
                        RemoveReservation(admission);
                    }
                }

                room.LastActiveTime = Now;
                return new()
                {
                    Success = true,
                    MasterUserId = room.MasterUserId,
                    IsEntryLocked = room.IsEntryLocked,
                    Message = room.IsEntryLocked ? "새 참가자의 입장을 잠갔습니다." : "새 참가자의 입장을 열었습니다."
                };
            }
        }

        // 현재 접속 중인 방장만 비밀번호를 바꿀 수 있습니다. 빈 문자열은 비밀번호 해제입니다.
        public BaseResponse UpdateRoomPassword(string userId, UpdateRoomPasswordRequest request)
        {
            lock (userService.MembershipLock)
            {
                if (string.IsNullOrWhiteSpace(request.LobbyId) ||
                    string.IsNullOrWhiteSpace(request.RoomId) ||
                    request.Password == null ||
                    !RoomPassword.Valid(request.Password))
                {
                    return Result(false, "비밀번호는 4~32자로 입력하거나 해제할 때 빈 문자열을 지정해주세요.");
                }

                var room = FindById(request.RoomId);
                if (room == null || room.LobbyId != request.LobbyId)
                {
                    return Result(false, "방을 찾을 수 없습니다.");
                }

                if (room.IsClosing || !IsConnectedMaster(room, userId))
                {
                    return Result(false, "현재 접속한 방장만 비밀번호를 변경할 수 있습니다.");
                }

                RoomPassword.Set(room, request.Password);
                int canceled = 0;
                // 이전 비밀번호로 허용된 예약이 새 설정을 우회하지 못하게 기존 예약을 모두 취소합니다.
                foreach (var admission in reservations.Values.Where(value => value.RoomId == room.Id).ToArray())
                {
                    RemoveReservation(admission);
                    canceled++;
                }

                room.LastActiveTime = Now;
                return Result(
                    true,
                    (room.HasPassword ? "방 비밀번호를 설정·변경했습니다." : "방 비밀번호를 해제했습니다.") + (canceled > 0 ? $" 기존 입장 예약 {canceled}개를 취소했습니다." : "")
                );
            }
        }
    }
}
