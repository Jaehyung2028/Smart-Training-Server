using System.Collections.Concurrent;

namespace Smart_Training_Server.Services.Room;

// Tracks the room registered on each dedicated-server SignalR connection.
// RoomService still validates the server secret and owns connection authority.
public sealed class RoomServerConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> roomsByConnection = new();

    public void AddConnection(string connectionId, string roomId) => roomsByConnection[connectionId] = roomId;
    public bool TryGetRoomId(string connectionId, out string? roomId) => roomsByConnection.TryGetValue(connectionId, out roomId);
    public bool RemoveConnection(string connectionId, out string? roomId) => roomsByConnection.TryRemove(connectionId, out roomId);
}
