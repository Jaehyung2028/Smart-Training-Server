using Smart_Training_Server.Helper;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

namespace Smart_Training_Server.Services.Operations;

public sealed record OperationalServer(string Kind, string Id, string LobbyId, string Name, string State,
    int ConnectedUsers, int PendingUsers, int RoomCount, int? MaxUsers, bool TrainingRunning,
    int? ProcessId, bool ProcessRunning, ProcessLifecycle? Lifecycle, LobbyMaintenance? Maintenance = null, LobbyShutdown? Shutdown = null, LobbyOperatingMode? Operation = null);
public sealed record ServerOperationsSnapshot(DateTime GeneratedAtUtc, DateTime StartedAtUtc,
    double UptimeSeconds, IReadOnlyList<OperationalServer> Lobbies, IReadOnlyList<OperationalServer> Rooms,
    IReadOnlyList<ProcessLifecycle> RecentProcesses, IReadOnlyList<ServerOperationEvent> RecentEvents, OperationsHistoryStatus History);

public sealed class ServerOperationsService(UserService users, LobbyService lobbies, RoomService rooms,
    ServerOperationsRegistry registry)
{
    public MaintenanceChangeResult SetMaintenance(string lobbyId, bool enabled, string? reason, long expectedRevision) =>
        lobbies.SetMaintenance(lobbyId, enabled, reason, expectedRevision);
    public ServerOperationsSnapshot GetSnapshot()
    {
        lock (users.MembershipLock)
        {
            var history = registry.Snapshot();
            var lifecycle = history.Processes.ToDictionary(x => x.Key);
            var lobbyRows = lobbies.GetLobbySummaries().Select(x =>
            {
                var process = ProcessHelper.GetProcessStatus("lobby:" + x.Id);
                return new OperationalServer("lobby", x.Id, x.Id, x.Name, x.ServerState,
                    x.UserCount, x.PendingUsers, x.RoomCount, null, false, process.Id, process.Running,
                    lifecycle.GetValueOrDefault("lobby:" + x.Id), registry.GetMaintenance(x.Id), registry.GetShutdown(x.Id), registry.GetOperatingMode(x.Id));
            }).ToArray();
            var roomRows = rooms.GetOperationsRooms().Select(x =>
            {
                var process = ProcessHelper.GetProcessStatus(x.Id);
                return new OperationalServer("room", x.Id, x.LobbyId, x.Name, x.State.ToString().ToLowerInvariant(),
                    x.ConnectedUsers, x.ReservedUsers, 0, x.MaxUsers, x.IsRunning, process.Id, process.Running,
                    lifecycle.GetValueOrDefault("room:" + x.Id));
            }).ToArray();
            var now = DateTime.UtcNow;
            return new(now, registry.StartedAtUtc, Math.Max(0, (now - registry.StartedAtUtc).TotalSeconds),
                lobbyRows, roomRows, history.Processes, history.Events, registry.HistoryStatus());
        }
    }
}
