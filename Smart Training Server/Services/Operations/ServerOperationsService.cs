using Smart_Training_Server.Helper;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

// 로비·방·운영 이력·실제 프로세스 상태를 모아 운영 페이지용 스냅샷을 만듭니다.
namespace Smart_Training_Server.Services.Operations
{
    // 운영 표 한 행입니다. 논리 상태·인원과 실제 프로세스 PID·생존 여부를 함께 표시합니다.
    public sealed record OperationalServer(
        string Kind,
        string Id,
        string LobbyId,
        string Name,
        string State,
        int ConnectedUsers,
        int PendingUsers,
        int RoomCount,
        int? MaxUsers,
        bool TrainingRunning,
        int? ProcessId,
        bool ProcessRunning,
        ProcessLifecycle? Lifecycle,
        LobbyMaintenance? Maintenance = null,
        LobbyShutdown? Shutdown = null,
        LobbyOperatingMode? Operation = null
    );

    // 한 번의 운영 조회 결과입니다. 조회 시각·가동 시간·로비·방·최근 이벤트·저장 상태를 묶습니다.
    public sealed record ServerOperationsSnapshot(
        DateTime GeneratedAtUtc,
        DateTime StartedAtUtc,
        double UptimeSeconds,
        IReadOnlyList<OperationalServer> Lobbies,
        IReadOnlyList<OperationalServer> Rooms,
        IReadOnlyList<ProcessLifecycle> RecentProcesses,
        IReadOnlyList<ServerOperationEvent> RecentEvents,
        OperationsHistoryStatus History
    );

    public sealed class ServerOperationsService
    {
        private readonly RoomService rooms;
        private readonly UserService users;
        private readonly LobbyService lobbies;
        private readonly ServerOperationsRegistry registry;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public ServerOperationsService(
            UserService users,
            LobbyService lobbies,
            RoomService rooms,
            ServerOperationsRegistry registry
        )
        {
            this.rooms = rooms;
            this.users = users;
            this.lobbies = lobbies;
            this.registry = registry;
        }

        // 로비·방 요약과 실제 프로세스 PID·실행 여부, 운영 이력을 합쳐 운영 화면 데이터를 만듭니다.
        public ServerOperationsSnapshot GetSnapshot()
        {
            lock (users.MembershipLock)
            {
                var history = registry.Snapshot();
                var lifecycle = history.Processes.ToDictionary(x => x.Key);
                var lobbyRows = lobbies.GetLobbySummaries().Select(x =>
                {
                    return new OperationalServer(
                        "lobby",
                        x.Id,
                        x.Id,
                        x.Name,
                        x.OperatingMode,
                        x.UserCount,
                        0,
                        x.RoomCount,
                        null,
                        false,
                        null,
                        false,
                        null,
                        registry.GetMaintenance(x.Id),
                        registry.GetShutdown(x.Id),
                        registry.GetOperatingMode(x.Id)
                    );
                }).ToArray();
                var roomRows = rooms.GetOperationsRooms().Select(x =>
                {
                    var process = ProcessHelper.GetProcessStatus(x.Id);
                    return new OperationalServer(
                        "room",
                        x.Id,
                        x.LobbyId,
                        x.Name,
                        x.State.ToString().ToLowerInvariant(),
                        x.ConnectedUsers,
                        x.ReservedUsers,
                        0,
                        x.MaxUsers,
                        x.IsRunning,
                        process.Id,
                        process.Running,
                        lifecycle.GetValueOrDefault("room:" + x.Id)
                    );
                }).ToArray();
                var now = DateTime.UtcNow;
                return new(
                    now,
                    registry.StartedAtUtc,
                    Math.Max(0, (now - registry.StartedAtUtc).TotalSeconds),
                    lobbyRows,
                    roomRows,
                    history.Processes,
                    history.Events,
                    registry.HistoryStatus()
                );
            }
        }
    }
}
