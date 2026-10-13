using Smart_Training_Server.Enum;

namespace Smart_Training_Server.Models
{
    public class ServerConnectionInfo
    {
        public string ServerId { get; set; } = string.Empty;

        public ServerType ServerType { get; set; }
    }
}
