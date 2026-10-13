namespace Smart_Training_Server.Requests.Lobby
{
    public class LeaveLobbyRequest
    {
        public string LobbyId { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
    }
}
