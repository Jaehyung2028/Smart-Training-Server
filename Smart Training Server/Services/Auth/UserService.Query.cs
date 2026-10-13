using Smart_Training_Server.Responses.Auth;

namespace Smart_Training_Server.Services.Auth;

public partial class UserService
{
    public CurrentUserResponse? GetCurrentUser(string userId)
    {
        lock (MembershipLock)
        {
            var user = FindById(userId);
            return user == null ? null : new CurrentUserResponse
            {
                Id = user.Id,
                Name = user.Name,
                CurrentLobbyId = user.CurrentLobbyId,
                CurrentLobbyName = user.CurrentLobbyName,
                LobbyIndex = user.LobbyIndex,
                CurrentRoomId = user.CurrentRoomId,
                CurrentRoomName = user.CurrentRoomName,
                RoomIndex = user.RoomIndex,
                PendingRoomId = user.PendingRoomId
            };
        }
    }
}
