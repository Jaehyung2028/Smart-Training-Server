using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room;

public sealed class RoomMasterResponse : BaseResponse
{
    public string MasterUserId { get; set; } = string.Empty;
    public bool IsEntryLocked { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int MaxUsers { get; set; }
    public bool IsRunning { get; set; }
    public long PhaseRevision { get; set; }
}
