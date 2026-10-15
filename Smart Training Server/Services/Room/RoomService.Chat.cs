namespace Smart_Training_Server.Services.Room;

public partial class RoomService
{
    internal bool CanUseRoomChat(string userId, string roomId, string admissionToken)
    {
        lock (userService.MembershipLock)
        {
            if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(admissionToken)) return false;
            var room = FindById(roomId);
            var user = userService.FindById(userId);
            return room != null && !room.IsClosing && user != null && user.CurrentRoomId == room.Id &&
                user.CurrentLobbyId == room.LobbyId && room.Users.ContainsKey(userId) && serverConnections.ContainsKey(roomId) &&
                connectedTokens.TryGetValue(userId, out var current) && current == admissionToken;
        }
    }
}
