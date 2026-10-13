using Smart_Training_Server.Enum;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Models
{
    public class LobbyInfo
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public ServerState State { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime LastActiveTime { get; set; }

        public ConcurrentDictionary<string, UserInfo> Users { get; set; } = new();

        public ConcurrentDictionary<string, RoomInfo> Rooms { get; set; } = new();

        public ConcurrentDictionary<string, ContentInfo> Contents { get; set; } = new();
    }
}
