namespace Smart_Training_Server.Services.Operations;

public sealed record LobbyOperatingMode(string LobbyId, string Mode, string Reason, DateTime? ShutdownAtUtc, long Revision, DateTime? ChangedAtUtc);
public sealed record ModeChangeResult(bool Success, int StatusCode, string Message, LobbyOperatingMode? Operation = null, string? PersistenceWarning = null);

public sealed partial class ServerOperationsRegistry
{
    private readonly Dictionary<string, LobbyOperatingMode> modes = new();
    public LobbyOperatingMode GetOperatingMode(string id)
    { lock (gate) return modes.GetValueOrDefault(id) ?? new(id, "running", "", null, 0, null); }

    public ModeChangeResult SetOperatingMode(string id, string name, string mode, string reason, DateTime? at, long expectedRevision)
    {
        lock (gate)
        {
            if (closing) return new(false, 503, "서버 종료 중에는 운영 설정을 변경할 수 없습니다.");
            if (mode is not ("running" or "maintenance" or "scheduled") || (mode == "scheduled") != (at != null))
                return new(false, 400, "운영 방식과 종료 시각을 확인해주세요.");
            var current = GetOperatingMode(id);
            if (current.Mode == mode && current.Reason == reason && current.ShutdownAtUtc == at)
                return new(true, 200, "이미 같은 운영 설정입니다.", current);
            if (current.Revision != expectedRevision) return new(false, 409, "운영 설정이 변경되었습니다. 최신 상태에서 다시 선택해주세요.");
            var next = new LobbyOperatingMode(id, mode, reason, at, current.Revision + 1, DateTime.UtcNow);
            modes[id] = next;
            AddModeEvent(id, name, mode == "running" ? "maintenance_off" : mode == "scheduled" ? "shutdown_scheduled" : "maintenance_on", reason);
            return new(true, 200, mode == "running" ? "운영을 재개했습니다. 점검과 종료 예약을 모두 해제했습니다." : mode == "scheduled" ? "예약 종료를 설정했습니다. 새 입장을 차단합니다." : "입장만 차단하도록 점검을 설정했습니다. 기존 접속은 유지합니다.", next);
        }
    }
    private void AddModeEvent(string id, string name, string action, string reason)
    {
        events.Enqueue(new(DateTime.UtcNow, "lobby", id, id, name, action, reason));
        while (events.Count > 200) events.Dequeue();
        revision++;
    }
}
