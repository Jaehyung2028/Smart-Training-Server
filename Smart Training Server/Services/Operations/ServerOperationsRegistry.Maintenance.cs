namespace Smart_Training_Server.Services.Operations;
public sealed record LobbyMaintenance(string LobbyId, bool Enabled, string Reason, DateTime? ChangedAtUtc, long Revision);
public sealed record MaintenanceChangeResult(bool Success, int StatusCode, string Message, LobbyMaintenance? Maintenance = null, string? PersistenceWarning = null);
public sealed partial class ServerOperationsRegistry
{
    // Compatibility projections share one state and one revision with scheduled shutdown.
    public LobbyMaintenance GetMaintenance(string id)
    { var mode = GetOperatingMode(id); return new(id, mode.Mode != "running", mode.Reason, mode.ChangedAtUtc, mode.Revision); }
    public MaintenanceChangeResult SetMaintenance(string id, string name, bool enabled, string reason, long expectedRevision)
    {
        var result = SetOperatingMode(id, name, enabled ? "maintenance" : "running", reason, null, expectedRevision);
        return new(result.Success, result.StatusCode, result.Message, result.Success ? GetMaintenance(id) : null);
    }
}