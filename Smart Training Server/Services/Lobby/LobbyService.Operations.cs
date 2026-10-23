// 로비 운영 요청을 처리합니다. 운영 종료 시 사용자 소속을 해제합니다.
namespace Smart_Training_Server.Services.Lobby
{
    public partial class LobbyService
    {
        // MembershipLock 안에서 호출합니다. 로비 운영 종료 시 사용자 소속을 해제합니다.
        internal void EvacuateLobby(string id)
        {
            var lobby = FindById(id);
            if (lobby == null)
            {
                return;
            }

            foreach (var user in lobby.Users.Values)
            {
                user.CurrentLobbyId = "";
                user.CurrentLobbyName = "";
                user.LobbyIndex = -1;
            }

            lobby.Users.Clear();
        }
    }
}
