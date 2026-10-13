using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room
{
    public class CreateRoomResponse : BaseResponse
    {
        public RoomInfo? Room { get; set; }
    }
}
