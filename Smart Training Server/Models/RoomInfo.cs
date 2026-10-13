using Smart_Training_Server.Enum;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Models
{
    public class RoomInfo
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string CreatorUserId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ServerToken { get; set; } = string.Empty;

        public int ReservedUsers { get; set; }
        public bool IsEntryLocked { get; set; }

        public ContentInfo Content { get; set; } = new();

        public string Id { get; set; } = string.Empty;

        public string LobbyId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public ServerState State { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime LastActiveTime { get; set; }

        public int MaxUsers { get; set; }

        public string MasterUserId { get; set; } = string.Empty;

        public ConcurrentDictionary<string, UserInfo> Users { get; set; } = new();
    }
}
