using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;
namespace Smart_Training_Server.Services.Operations;

public sealed class LobbyShutdownService(UserService users, LobbyService lobbies, RoomService rooms, ServerOperationsRegistry registry)
{
    public ModeChangeResult SetOperatingMode(string id, string? mode, DateTimeOffset? at, string? reason, long expectedRevision)
    {
        ModeChangeResult result;
        lock (users.MembershipLock)
        {
            var lobby = lobbies.FindById(id);
            if (lobby?.IsCatalogLobby != true) return new(false, 404, "카탈로그 로비를 찾을 수 없습니다.");
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200 || expectedRevision < 0 || mode is not ("running" or "maintenance" or "scheduled") ||
                (mode == "scheduled") != (at != null)) return new(false, 400, "운영 방식·사유·종료 시각을 확인해주세요.");
            if (at != null && (at <= DateTimeOffset.UtcNow || at > DateTimeOffset.UtcNow.AddDays(30))) return new(false, 400, "종료 시각은 현재 이후부터 30일 이내로 지정해주세요.");
            result = registry.SetOperatingMode(id, lobby.Name, mode, reason, at?.UtcDateTime, expectedRevision);
        }
        if (result.Success)
        {
            registry.FlushHistory();
            var history = registry.HistoryStatus();
            if (!history.Enabled || history.Error != null) result = result with { PersistenceWarning = "현재 실행에는 적용됐지만 파일 저장이 확인되지 않았습니다." };
        }
        return result;
    }
    public ShutdownChangeResult SetShutdown(string id, DateTimeOffset? at, string? reason, long expectedRevision)
    {
        var result = SetOperatingMode(id, at == null ? "running" : "scheduled", at, reason, expectedRevision);
        return new(result.Success, result.StatusCode, result.Message, result.Success ? registry.GetShutdown(id) : null, result.PersistenceWarning);
    }
    public void ExecuteDue(DateTime utcNow)
    {
        bool changed = false;
        lock (users.MembershipLock)
            foreach (var lobby in lobbies.GetAll().Where(x => x.IsCatalogLobby))
            {
                var schedule = registry.GetShutdown(lobby.Id);
                if (schedule.State != "scheduled" || schedule.AtUtc > utcNow) continue;
                rooms.CloseLobbyRooms(lobby.Id);
                lobbies.EvacuateLobby(lobby.Id);
                registry.CompleteShutdown(lobby.Id, lobby.Name);
                changed = true;
            }
        if (changed) registry.FlushHistory();
    }
}
public sealed class LobbyShutdownWorker(LobbyShutdownService service, ILogger<LobbyShutdownWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do { try { service.ExecuteDue(DateTime.UtcNow); } catch (Exception error) { logger.LogError(error, "Lobby shutdown failed; retrying"); } }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
