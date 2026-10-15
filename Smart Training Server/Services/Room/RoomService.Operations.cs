using Smart_Training_Server.Responses.Room;

namespace Smart_Training_Server.Services.Room;
public partial class RoomService
{
    // Unlike public room queries, this snapshot does not expire reservations or change state.
    public IReadOnlyList<RoomSummaryResponse> GetOperationsRooms()
    {
        lock (userService.MembershipLock)
            return rooms.Values.OrderBy(x => x.LobbyId).ThenBy(x => x.Id).Select(ToRoomSummary).ToArray();
    }
}
