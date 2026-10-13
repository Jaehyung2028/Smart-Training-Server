namespace Smart_Training_Server.Models
{
    public class UserInfo
    {
        public string PendingRoomId { get; set; } = string.Empty;

        public int LobbyIndex { get; set; } = -1;

        public int RoomIndex { get; set; } = -1;

        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string CurrentLobbyId { get; set; } = string.Empty;

        public string CurrentLobbyName { get; set; } = string.Empty;

        public string CurrentRoomId { get; set; } = string.Empty;

        public string CurrentRoomName { get; set; } = string.Empty;
    }
}
