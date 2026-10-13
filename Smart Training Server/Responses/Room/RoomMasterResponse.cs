using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room;

public sealed class RoomMasterResponse : BaseResponse
{
    public string MasterUserId { get; set; } = string.Empty;
    public bool IsEntryLocked { get; set; }
}
