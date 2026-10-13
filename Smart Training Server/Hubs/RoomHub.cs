using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Room;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Hubs
{
    public class RoomHub : Hub
    {
        private readonly RoomService roomService;
        private readonly ServerConnectionService connectionService;

        public RoomHub(
            RoomService roomService,
            ServerConnectionService connectionService)
        {
            this.roomService = roomService;
            this.connectionService = connectionService;
        }

        public Task Register(string roomId, string serverToken)
        {
            if (connectionService.TryGetConnection(Context.ConnectionId, out var existing) &&
                existing != null && existing.ServerId != roomId)
                throw new HubException("이미 다른 방 서버로 등록된 연결입니다.");
            if (!roomService.RegisterServer(roomId, serverToken, Context.ConnectionId))
                throw new HubException("방 서버 등록 정보가 유효하지 않습니다.");
            connectionService.AddConnection(
                Context.ConnectionId,
                roomId,
                ServerType.Room);

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
            if (!connectionService.TryGetConnection(Context.ConnectionId, out var connection) ||
                connection == null || connection.ServerType != ServerType.Room)
                throw new HubException("먼저 방 서버를 등록해주세요.");
            return connection.ServerId;
        }

        public Smart_Training_Server.Responses.Room.RoomMasterResponse GetRoomMaster()
        {
            return roomService.GetRoomMaster(GetRoomId(), Context.ConnectionId);
        }

        public override async Task OnDisconnectedAsync(
            Exception? exception)
        {
            if (connectionService.RemoveConnection(
                Context.ConnectionId,
                out var connection) && connection != null)
            {
                roomService.ServerDisconnected(connection.ServerId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
