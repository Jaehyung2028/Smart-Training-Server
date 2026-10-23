using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Room;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Services.Room;

// 등록된 Unity 방 서버의 예약 확인·접속 보고·방 상태 연동을 받는 SignalR 창구입니다.
namespace Smart_Training_Server.Hubs
{
    // Unity 방 서버와 ASP.NET의 관리 통신 창구입니다. 플레이어 이동 동기화는 Unity Netcode가 담당합니다.
    public class RoomHub : Hub
    {
        private readonly RoomService roomService;
        private readonly RoomServerConnectionRegistry connectionService;

        // Hub가 사용할 방 서비스와 연결 대응표를 의존성 주입으로 전달받습니다.
        public RoomHub(RoomService roomService, RoomServerConnectionRegistry connectionService)
        {
            this.roomService = roomService;
            this.connectionService = connectionService;
        }

        // SignalR 연결 ID를 하나의 방 서버와 연결합니다. 이후 호출은 이 등록 정보로 소속 방을 확인합니다.
        public Task Register(string roomId, string serverToken)
        {
            if (connectionService.TryGetRoomId(Context.ConnectionId, out var existing) && existing != roomId)
            {
                throw new HubException("이미 다른 방 서버로 등록된 연결입니다.");
            }

            if (!roomService.RegisterServer(roomId, serverToken, Context.ConnectionId))
            {
                throw new HubException("방 서버 등록 정보가 유효하지 않습니다.");
            }

            connectionService.AddConnection(Context.ConnectionId, roomId);
            return Task.CompletedTask;
        }

        // 실제 연결 완료를 보고받아 ASP.NET의 예약 인원을 접속 인원으로 전환합니다.
        public BaseResponse ConfirmConnection(string userId, string admissionToken)
        {
            return roomService.ConfirmConnection(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        // Netcode 연결 승인 전 호출합니다. 예약이 유효한지만 확인하고 아직 참가자를 추가하지 않습니다.
        public BaseResponse ValidateConnection(string userId, string admissionToken)
        {
            return roomService.ValidateConnection(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        // 등록된 Unity 서버가 보고한 접속 해제를 RoomService로 전달합니다. 토큰으로 어떤 연결의 종료인지 구분합니다.
        public BaseResponse ClientDisconnected(string userId, string admissionToken)
        {
            return roomService.ClientDisconnected(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        // 호출자가 보낸 방 ID를 매번 신뢰하는 대신 현재 등록된 SignalR 연결에서 대상 방을 찾습니다.
        private string GetRoomId()
        {
            if (!connectionService.TryGetRoomId(Context.ConnectionId, out var roomId) || roomId == null)
            {
                throw new HubException("먼저 방 서버를 등록해주세요.");
            }

            return roomId;
        }

        // 등록된 Unity 서버가 자신의 방의 최신 방장·설정·진행 상태를 받아갑니다.
        public RoomMasterResponse GetRoomMaster()
        {
            return roomService.GetRoomMaster(GetRoomId(), Context.ConnectionId);
        }

        // 문자열로 전달된 진행 여부와 버전을 파싱한 뒤 방 서비스에 상태 변경 검사를 요청합니다.
        public RoomMasterResponse SetRoomPhase(
            string userId,
            string token,
            string running,
            string rosterJson,
            string expectedRevision
        )
        {
            if (!bool.TryParse(running, out var value))
            {
                throw new HubException("진행 상태가 올바르지 않습니다.");
            }

            if (!long.TryParse(
                expectedRevision,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var revision
            ))
            {
                throw new HubException("상태 버전이 올바르지 않습니다.");
            }

            return roomService.SetRoomPhase(GetRoomId(), Context.ConnectionId, userId, token, value, rosterJson, revision);
        }

        // 현재 SignalR 연결 종료를 정리합니다. 서버 Hub는 서버 상태를, 사용자 Hub는 구독 정보를 정리합니다.
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (connectionService.RemoveConnection(Context.ConnectionId, out var roomId) && roomId != null)
            {
                roomService.ServerDisconnected(roomId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
