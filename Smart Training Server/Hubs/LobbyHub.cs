using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Lobby;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Hubs
{
    public class LobbyHub : Hub
    {
        private readonly LobbyService lobbyService;
        private readonly ServerConnectionService connectionService;

        public LobbyHub(
            LobbyService lobbyService,
            ServerConnectionService connectionService)
        {
            this.lobbyService = lobbyService;
            this.connectionService = connectionService;
        }

        public Task Register(string lobbyId)
        {
            connectionService.AddConnection(
                Context.ConnectionId,
                lobbyId,
                ServerType.Lobby);

            lobbyService.OnReady(lobbyId);

            return Task.CompletedTask;
        }

        public override async Task OnDisconnectedAsync(
            Exception? exception)
        {
            if (connectionService.RemoveConnection(
                Context.ConnectionId,
                out var connection))
            {
                lobbyService.OnDisconnected(connection.ServerId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
