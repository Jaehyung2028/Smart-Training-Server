using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room
{
    public class JoinRoomResponse : BaseResponse
    {
        public RoomInfo? Room { get; set; }
        public string AdmissionToken { get; set; } = string.Empty;
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
