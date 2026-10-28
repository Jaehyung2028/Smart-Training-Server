using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Room
{
    public partial class RoomService
    {
        // 실제 접속한 방장만 대기 중에 변경합니다. 예약 중인 사용자에게 다른 콘텐츠가 안내되는 것도 막습니다.
        public BaseResponse ChangeRoomContent(string userId, ChangeRoomContentRequest request)
        {
            lock (userService.MembershipLock)
            {
                ExpireReservations();
                var room = FindById(request.RoomId);
                if (room == null || room.LobbyId != request.LobbyId || room.IsClosing)
                {
                    return Result(false, "방을 찾을 수 없습니다.");
                }

                if (!IsConnectedMaster(room, userId))
                {
                    return Result(false, "현재 접속한 방장만 콘텐츠를 변경할 수 있습니다.");
                }

                if (room.IsRunning || room.ReservedUsers > 0)
                {
                    return Result(false, "대기 중이며 참가자의 접속 예약이 없을 때 콘텐츠를 변경할 수 있습니다.");
                }

                var lobby = lobbyService.FindById(room.LobbyId);
                if (lobby == null || !lobby.Contents.TryGetValue(request.ContentId, out var root))
                {
                    return Result(false, "현재 교육과정의 콘텐츠를 선택해주세요.");
                }

                var content = FindContentNode(root, request.ContentNodeId);
                if (content == null || content.Children.Count > 0)
                {
                    return Result(false, "하위 항목이 없는 최하위 콘텐츠를 선택해주세요.");
                }

                if (ReferenceEquals(room.Content, content))
                {
                    return Result(true, "이미 선택된 콘텐츠입니다.");
                }

                room.Content = content;
                // Unity 서버가 이전 콘텐츠로 보낸 시작 요청은 버전 검사에서 거절됩니다.
                room.PhaseRevision++;
                room.LastActiveTime = Now;
                return Result(true, "콘텐츠를 변경했습니다. 참가자의 준비와 학습 상태가 초기화됩니다.");
            }
        }
    }
}
