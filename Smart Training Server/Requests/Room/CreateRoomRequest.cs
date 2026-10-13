namespace Smart_Training_Server.Requests.Room
{
    public class CreateRoomRequest
    {
        public string UserId { get; set; } = string.Empty;

        public string LobbyId { get; set; } = string.Empty;

        public string RoomName { get; set; } = string.Empty;

        public string ContentId { get; set; } = string.Empty;

        public int MaxUsers { get; set; }
    }
}
