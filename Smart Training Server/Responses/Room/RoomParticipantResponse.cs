using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room;

public sealed class RoomParticipantResponse : BaseResponse
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int RoomIndex { get; set; }
}
