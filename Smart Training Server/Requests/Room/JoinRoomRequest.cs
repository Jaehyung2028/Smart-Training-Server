namespace Smart_Training_Server.Requests.Room
{
    public class JoinRoomRequest
    {
        public string? Password { get; set; }
        public string UserId { get; set; } = string.Empty;

        public string LobbyId { get; set; } = string.Empty;

        public string RoomId { get; set; } = string.Empty;
    }
}
