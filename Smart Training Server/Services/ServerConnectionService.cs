using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Services
{
    public class ServerConnectionService
    {
        private readonly ConcurrentDictionary<string, ServerConnectionInfo> connections = new();

        public void AddConnection(
            string connectionId,
            string serverId,
            ServerType serverType)
        {
            connections[connectionId] = new ServerConnectionInfo
            {
                ServerId = serverId,
                ServerType = serverType
            };
        }

        public bool RemoveConnection(
            string connectionId,
            out ServerConnectionInfo? connection)
        {
            return connections.TryRemove(
                connectionId,
                out connection);
        }

        public bool TryGetConnection(
            string connectionId,
            out ServerConnectionInfo? connection)
        {
            return connections.TryGetValue(
                connectionId,
                out connection);
        }
    }
}
