namespace Smart_Training_Server.Responses.Lobby;

public class LobbySummaryResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ContentId { get; init; } = string.Empty;
    public string ServerState { get; init; } = "offline";
    public int PendingUsers { get; init; }
    public DateTime? ShutdownAtUtc { get; init; }
    public string ShutdownReason { get; init; } = "";
    public string ShutdownState { get; init; } = "none";
    public long ShutdownRevision { get; init; }
    public string OperatingMode { get; init; } = "running"; public string OperatingReason { get; init; } = ""; public long OperatingRevision { get; init; } public bool IsMaintenance { get; init; }
    public string MaintenanceReason { get; init; } = string.Empty;
    public DateTime? MaintenanceChangedAtUtc { get; init; }
    public long MaintenanceRevision { get; init; }
    public int UserCount { get; init; }
    public int RoomCount { get; init; }
    public int ContentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
