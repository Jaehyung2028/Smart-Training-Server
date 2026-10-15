using Microsoft.AspNetCore.SignalR;

using Smart_Training_Server.Services.Room;

namespace Smart_Training_Server.Hubs
{
    public class RoomHub : Hub
    {
        private readonly RoomService roomService;
        private readonly RoomServerConnectionRegistry connectionService;

        public RoomHub(
            RoomService roomService,
            RoomServerConnectionRegistry connectionService)
        {
            this.roomService = roomService;
            this.connectionService = connectionService;
        }

        public Task Register(string roomId, string serverToken)
        {
            if (connectionService.TryGetRoomId(Context.ConnectionId, out var existing) &&
                existing != roomId)
                throw new HubException("이미 다른 방 서버로 등록된 연결입니다.");
            if (!roomService.RegisterServer(roomId, serverToken, Context.ConnectionId))
                throw new HubException("방 서버 등록 정보가 유효하지 않습니다.");
            connectionService.AddConnection(Context.ConnectionId, roomId);

            return Task.CompletedTask;
        }

        public Smart_Training_Server.Responses.Base.BaseResponse ConfirmConnection(string userId, string admissionToken)
        {
            return roomService.ConfirmConnection(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        public Smart_Training_Server.Responses.Base.BaseResponse ValidateConnection(string userId, string admissionToken)
        {
            return roomService.ValidateConnection(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        public Smart_Training_Server.Responses.Base.BaseResponse ClientDisconnected(string userId, string admissionToken)
        {
            return roomService.ClientDisconnected(GetRoomId(), Context.ConnectionId, userId, admissionToken);
        }

        private string GetRoomId()
        {
            if (!connectionService.TryGetRoomId(Context.ConnectionId, out var roomId) || roomId == null)
                throw new HubException("먼저 방 서버를 등록해주세요.");
            return roomId;
        }

        public Smart_Training_Server.Responses.Room.RoomMasterResponse GetRoomMaster()
        {
            return roomService.GetRoomMaster(GetRoomId(), Context.ConnectionId);
        }

        public Smart_Training_Server.Responses.Room.RoomMasterResponse SetRoomPhase(string userId, string token, string running, string rosterJson, string expectedRevision)
        {
            if (!bool.TryParse(running, out var value)) throw new HubException("진행 상태가 올바르지 않습니다.");
            if (!long.TryParse(expectedRevision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision)) throw new HubException("상태 버전이 올바르지 않습니다.");
            return roomService.SetRoomPhase(GetRoomId(), Context.ConnectionId, userId, token, value, rosterJson, revision);
        }

        public override async Task OnDisconnectedAsync(
            Exception? exception)
        {
            if (connectionService.RemoveConnection(
                Context.ConnectionId,
                out var roomId) && roomId != null)
            {
                roomService.ServerDisconnected(roomId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}


