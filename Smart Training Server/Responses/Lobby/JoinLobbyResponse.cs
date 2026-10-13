using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Lobby
{
    public class JoinLobbyResponse : BaseResponse
    {
        public LobbyInfo? Lobby { get; set; }
    }
}
