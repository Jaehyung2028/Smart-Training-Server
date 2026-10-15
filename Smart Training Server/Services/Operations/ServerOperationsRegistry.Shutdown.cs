namespace Smart_Training_Server.Services.Operations;
public sealed record LobbyShutdown(string LobbyId, DateTime? AtUtc, string Reason, string State, long Revision, DateTime? ChangedAtUtc);
public sealed record ShutdownChangeResult(bool Success, int StatusCode, string Message, LobbyShutdown? Shutdown = null, string? PersistenceWarning = null);
public sealed partial class ServerOperationsRegistry
{
    public LobbyShutdown GetShutdown(string id)
    { var mode = GetOperatingMode(id); return new(id, mode.ShutdownAtUtc, mode.Reason, mode.Mode == "scheduled" ? "scheduled" : mode.Mode == "maintenance" && mode.ShutdownAtUtc != null ? "completed" : "none", mode.Revision, mode.ChangedAtUtc); }
    public ShutdownChangeResult SetShutdown(string id, string name, DateTime? atUtc, string reason, long expectedRevision)
    {
        var result = SetOperatingMode(id, name, atUtc == null ? "running" : "scheduled", reason, atUtc, expectedRevision);
        return new(result.Success, result.StatusCode, result.Message, result.Success ? GetShutdown(id) : null);
    }
    public void CompleteShutdown(string id, string name)
    {
        lock (gate)
        {
            if (closing) return;
            var current = GetOperatingMode(id);
            if (current.Mode != "scheduled") return;
            modes[id] = current with { Mode = "maintenance", Revision = current.Revision + 1, ChangedAtUtc = DateTime.UtcNow };
            AddModeEvent(id, name, "shutdown_completed", current.Reason);
        }
    }
}