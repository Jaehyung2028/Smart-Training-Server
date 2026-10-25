using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Lobby;

// 인증된 Unity 클라이언트의 목록 알림과 로비·방 채팅 요청을 받는 SignalR 창구입니다.
namespace Smart_Training_Server.Hubs
{
    [Authorize]
    // 인증된 클라이언트가 목록 변경과 채팅을 구독하는 창구입니다. Unity 전용 서버용 RoomHub와 구분합니다.
    public sealed class LobbyUpdatesHub : Hub
    {
        private readonly LobbyRoomUpdates updates;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public LobbyUpdatesHub(LobbyRoomUpdates updates)
        {
            this.updates = updates;
        }

        // 현재 방 소속과 입장 토큰을 검사하여 해당 연결에 방 채팅 수신 권한을 등록합니다.
        public async Task<RoomChatSubscriptionResponse> SubscribeRoomChat(string roomId, string admissionToken)
        {
            await updates.GroupGate.WaitAsync(Context.ConnectionAborted);
            try
            {
                var result = updates.SubscribeRoomChat(Context.ConnectionId, roomId, admissionToken);
                if (result.Success)
                {
                    await updates.SynchronizeRoomGroupsAsync(Groups);
                }

                return result;
            }
            finally
            {
                updates.GroupGate.Release();
            }
        }

        // 수신자에게 동일한 방 ID와 메시지 번호를 보내므로 방 그룹으로 한 번에 전송합니다.
        public async Task<BaseResponse> SendRoomChat(string roomId, string admissionToken, string text)
        {
            await updates.GroupGate.WaitAsync(Context.ConnectionAborted);
            try
            {
                var result = updates.PrepareRoomChat(Context.ConnectionId, roomId, admissionToken, text);
                if (result.Success)
                {
                    await updates.SynchronizeRoomGroupsAsync(Groups, result.GroupName);
                    await Clients.Group(result.GroupName!).SendAsync("RoomChat", result.Payload, roomId, Context.ConnectionAborted);
                }

                return new BaseResponse
                {
                    Success = result.Success,
                    Message = result.Message

                };
            }
            finally
            {
                updates.GroupGate.Release();
            }
        }

        // 만료·퇴장 연결을 그룹에서 제외하고, 검증한 채팅을 같은 로비 그룹에 전달합니다.
        public async Task<BaseResponse> SendChat(string text)
        {
            await updates.GroupGate.WaitAsync(Context.ConnectionAborted);
            try
            {
                var result = updates.PrepareChat(Context.ConnectionId, text);
                if (result.Success)
                {
                    await updates.SynchronizeGroupsAsync(Groups, result.GroupName);
                    await Clients.Group(result.GroupName!).SendAsync("LobbyChat", result.Payload, Context.ConnectionAborted);
                }

                return new BaseResponse
                {
                    Success = result.Success,
                    Message = result.Message

                };
            }
            finally
            {
                updates.GroupGate.Release();
            }
        }

        // 현재 로비 소속과 토큰을 확인해 목록 변경 알림 대상을 등록합니다.
        public async Task<BaseResponse> Subscribe(string lobbyId)
        {
            await updates.GroupGate.WaitAsync(Context.ConnectionAborted);
            try
            {
                var token = Context.GetHttpContext()!.Request.Headers.Authorization.ToString();
                if (!updates.Subscribe(Context.ConnectionId, lobbyId, token))
                {
                    throw new HubException("현재 입장한 로비만 구독할 수 있습니다.");
                }

                await updates.SynchronizeGroupsAsync(Groups);
                await updates.SynchronizeRoomGroupsAsync(Groups);
                return new BaseResponse
                {
                    Success = true
                };
            }
            finally
            {
                updates.GroupGate.Release();
            }
        }

        // SignalR 연결이 사라지면 해당 연결의 목록·채팅 구독을 제거합니다. 사용자 로그아웃 자체와는 구분합니다.
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await updates.GroupGate.WaitAsync();
            try
            {
                updates.Remove(Context.ConnectionId);
                await updates.SynchronizeGroupsAsync(Groups);
                await updates.SynchronizeRoomGroupsAsync(Groups);
            }
            finally
            {
                updates.GroupGate.Release();
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
