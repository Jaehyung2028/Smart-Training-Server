namespace Smart_Training_Server.Services.Room;
public partial class RoomService
{
    internal void CloseLobbyRooms(string lobbyId)
    {
        lock (userService.MembershipLock)
            foreach (var room in rooms.Values.Where(x => x.LobbyId == lobbyId).ToArray())
            {
                room.IsClosing = true;
                ResetRoomServer(room, "로비 종료 예약 실행");
                DeleteRoom(lobbyService.FindById(lobbyId), room);
            }
    }
}
