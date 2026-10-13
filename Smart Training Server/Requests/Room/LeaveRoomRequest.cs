namespace Smart_Training_Server.Requests.Room
{
    public class LeaveRoomRequest
    {
        public string LobbyId { get; set; } = string.Empty;

        public string RoomId { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
    }
}
