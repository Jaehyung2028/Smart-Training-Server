using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

// 운영 모드 입력을 검사하고 예약 시각이 된 로비의 방·참가자·프로세스를 정리합니다. 함께 정의된 Worker가 주기적으로 실행합니다.
namespace Smart_Training_Server.Services.Operations
{
    public sealed class LobbyOperatingModeService
    {
        private readonly UserService users;

        private readonly LobbyService lobbies;

        private readonly RoomService rooms;

        private readonly ServerOperationsRegistry registry;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public LobbyOperatingModeService(UserService users, LobbyService lobbies, RoomService rooms, ServerOperationsRegistry registry)
        {
            this.users = users;
            this.lobbies = lobbies;
            this.rooms = rooms;
            this.registry = registry;
        }

        // 운영 중·점검·예약 종료 중 하나로 바꿉니다. 입력·현재 버전 검사는 담당 서비스와 저장소에서 수행합니다.
        public ModeChangeResult SetOperatingMode(string id, string? mode, DateTimeOffset? at, string? reason, long expectedRevision)
        {
            ModeChangeResult result;
            lock (users.MembershipLock)
            {
                var lobby = lobbies.FindById(id);
                if (lobby == null)
                {
                    return new(false, 404, "카탈로그 로비를 찾을 수 없습니다.");
                }

                reason = reason?.Trim();
                if (string.IsNullOrWhiteSpace(reason) ||
                    reason.Length > 200 ||
                    expectedRevision < 0 ||
                    mode is not ("running" or "maintenance" or "scheduled") ||
                    (mode == "scheduled") != (at != null))
                {
                    return new(false, 400, "운영 방식·사유·종료 시각을 확인해주세요.");
                }

                if (at != null && (at <= DateTimeOffset.UtcNow || at > DateTimeOffset.UtcNow.AddDays(30)))
                {
                    return new(false, 400, "종료 시각은 현재 이후부터 30일 이내로 지정해주세요.");
                }

                result = registry.SetOperatingMode(id, lobby.Name, mode, reason, at?.UtcDateTime, expectedRevision);
            }

            if (result.Success)
            {
                registry.FlushHistory();
                var history = registry.HistoryStatus();
                if (!history.Enabled || history.Error != null)
                {
                    result = result with
                    {
                        PersistenceWarning = "현재 실행에는 적용됐지만 파일 저장이 확인되지 않았습니다."
                    };
                }
            }

            return result;
        }

        // 종료 예약 시각이 된 로비의 방과 사용자를 정리하고 종료 완료를 점검 상태로 기록합니다.
        public void ExecuteDue(DateTime utcNow)
        {
            bool changed = false;
            lock (users.MembershipLock)
            {
                foreach (var lobby in lobbies.GetAll())
                {
                    var schedule = registry.GetShutdown(lobby.Id);
                    if (schedule.State != "scheduled" || schedule.AtUtc > utcNow)
                    {
                        continue;
                    }

                    rooms.CloseLobbyRooms(lobby.Id);
                    lobbies.EvacuateLobby(lobby.Id);
                    registry.CompleteShutdown(lobby.Id, lobby.Name);
                    changed = true;
                }
            }

            if (changed)
            {
                registry.FlushHistory();
            }
        }
    }

    public sealed class LobbyShutdownWorker : BackgroundService
    {
        private readonly LobbyOperatingModeService service;

        private readonly ILogger<LobbyShutdownWorker> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public LobbyShutdownWorker(LobbyOperatingModeService service, ILogger<LobbyShutdownWorker> logger)
        {
            this.service = service;
            this.logger = logger;
        }

        // 종료 시각이 된 로비를 주기적으로 확인해 예약 종료를 실행합니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    try
                    {
                        service.ExecuteDue(DateTime.UtcNow);
                    }
                    catch (Exception error)
                    {
                        logger.LogError(error, "Lobby shutdown failed; retrying");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
