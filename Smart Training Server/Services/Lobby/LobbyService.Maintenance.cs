using Smart_Training_Server.Services.Operations;

namespace Smart_Training_Server.Services.Lobby;

public partial class LobbyService
{
    public MaintenanceChangeResult SetMaintenance(string lobbyId, bool enabled, string? reason, long expectedRevision)
    {
        MaintenanceChangeResult result;
        lock (userService.MembershipLock)
        {
            var lobby = FindById(lobbyId);
            if (lobby?.IsCatalogLobby != true) return new(false, 404, "카탈로그 로비를 찾을 수 없습니다.");
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200 || expectedRevision < 0)
                return new(false, 400, "변경 사유를 1~200자로 입력하고 현재 설정을 확인해주세요.");
            result = operations.SetMaintenance(lobbyId, lobby.Name, enabled, reason, expectedRevision);
        }
        // File IO must not hold the membership lock used by multiplayer admission.
        if (result.Success)
        {
            operations.FlushHistory();
            var history = operations.HistoryStatus();
            if (!history.Enabled || history.Error != null)
                result = result with { PersistenceWarning = "현재 서버에는 적용됐지만 파일 저장이 확인되지 않았습니다. 재시작 후 유지 여부를 확인해주세요." };
        }
        return result;
    }
}
