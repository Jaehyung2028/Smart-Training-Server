using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Services.Lobby;

namespace Smart_Training_Server.Hubs;

// Authenticated lobby snapshots, participants and chat notifications.
[Authorize]
public sealed class LobbyUpdatesHub(LobbyRoomUpdates updates) : Hub
{
    public object SubscribeRoomChat(string roomId, string admissionToken, string bindingId) =>
        new { success = updates.SubscribeRoomChat(Context.ConnectionId, roomId, admissionToken, bindingId) };

    public async Task<object> SendRoomChat(string roomId, string admissionToken, string bindingId, string text)
    {
        var result = updates.PrepareRoomChat(Context.ConnectionId, roomId, admissionToken, bindingId, text);
        if (result.Success)
            await Task.WhenAll(result.Recipients.Select(recipient => Clients.Client(recipient.ConnectionId)
                .SendAsync("RoomChat", result.Payload, recipient.BindingId, Context.ConnectionAborted)));
        return new { success = result.Success, message = result.Message };
    }

    public async Task<object> SendChat(string text)
    {
        var result = updates.PrepareChat(Context.ConnectionId, text);
        if (result.Success)
            await Clients.Clients(result.Recipients).SendAsync("LobbyChat", result.Payload, Context.ConnectionAborted);
        return new { success = result.Success, message = result.Message };
    }

    public object Subscribe(string lobbyId)
    {
        var token = Context.GetHttpContext()!.Request.Headers.Authorization.ToString();
        if (!updates.Subscribe(Context.ConnectionId, lobbyId, token))
            throw new HubException("현재 입장한 로비만 구독할 수 있습니다.");
        return new { success = true };
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        updates.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

