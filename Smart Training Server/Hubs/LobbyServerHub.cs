using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Services.Lobby;
namespace Smart_Training_Server.Hubs;
public sealed class LobbyServerHub(LobbyService lobbies) : Hub
{
    public BaseResponse Register(string lobbyId, string secret)
    {
        if (Context.Items.TryGetValue("lobby", out var existing) && (string?)existing != lobbyId)
            return new() { Success = false, Message = "이미 다른 로비에 등록되어 있습니다." };
        bool ok = lobbies.RegisterLobbyServer(lobbyId, secret, Context.ConnectionId);
        if (ok) Context.Items["lobby"] = lobbyId;
        return new() { Success = ok, Message = ok ? "로비 서버가 준비되었습니다." : "로비 서버 등록을 확인할 수 없습니다." };
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("lobby", out var id) && id is string lobbyId) lobbies.LobbyServerDisconnected(lobbyId, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
