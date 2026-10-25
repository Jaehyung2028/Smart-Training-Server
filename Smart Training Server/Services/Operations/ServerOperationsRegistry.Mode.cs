// 운영 모드와 점검·종료 예약을 같은 상태 및 버전으로 관리합니다. 파일 저장은 Persistence 파일에서 처리합니다.
namespace Smart_Training_Server.Services.Operations
{
    // 로비의 통합 운영 상태입니다. with 표현식으로 필요한 값을 바꾼 새 record를 생성합니다.
    public sealed record LobbyOperatingMode(
string LobbyId,
string Mode,
string Reason,
DateTime? ShutdownAtUtc,
long Revision,
DateTime? ChangedAtUtc
);
    // 운영 변경의 성공 여부·HTTP 상태·설명·반영 결과·파일 저장 경고를 묶습니다.
    public sealed record ModeChangeResult(
bool Success,
int StatusCode,
string Message,
LobbyOperatingMode? Operation = null,
string? PersistenceWarning = null
);
    // 기존 점검 조회 형식입니다. 실제 값은 통합 운영 모드에서 계산합니다.
    public sealed record LobbyMaintenance(string LobbyId, bool Enabled, string Reason, DateTime? ChangedAtUtc, long Revision);
    // 종료 예약의 대상·시각·사유·상태·버전입니다.
    public sealed record LobbyShutdown(
string LobbyId,
DateTime? AtUtc,
string Reason,
string State,
long Revision,
DateTime? ChangedAtUtc
);
    public sealed partial class ServerOperationsRegistry
    {
        // 로비별 통합 운영 모드입니다. 점검과 종료 예약 정보는 여기서 파생합니다.
        private readonly Dictionary<string, LobbyOperatingMode> modes = new();

        // 저장된 로비 운영 모드를 반환합니다. 설정이 없으면 운영 중·버전 0을 기본값으로 사용합니다.
        public LobbyOperatingMode GetOperatingMode(string id)
        {
            lock (gate)
            {
                return modes.GetValueOrDefault(id) ?? new(id, "running", "", null, 0, null);
            }
        }

        // 운영 중·점검·예약 종료 중 하나로 바꿉니다. 입력·현재 버전 검사는 담당 서비스와 저장소에서 수행합니다.
        public ModeChangeResult SetOperatingMode(string id, string name, string mode, string reason, DateTime? at, long expectedRevision)
        {
            lock (gate)
            {
                if (closing)
                {
                    return new(false, 503, "서버 종료 중에는 운영 설정을 변경할 수 없습니다.");
                }

                if (mode is not ("running" or "maintenance" or "scheduled") || (mode == "scheduled") != (at != null))
                {
                    return new(false, 400, "운영 방식과 종료 시각을 확인해주세요.");
                }

                var current = GetOperatingMode(id);
                if (current.Mode == mode && current.Reason == reason && current.ShutdownAtUtc == at)
                {
                    return new(true, 200, "이미 같은 운영 설정입니다.", current);
                }

                // 다른 요청이 먼저 설정을 바꿨으면 409로 거절해 오래된 화면의 변경이 최신 상태를 덮지 못하게 합니다.
                if (current.Revision != expectedRevision)
                {
                    return new(false, 409, "운영 설정이 변경되었습니다. 최신 상태에서 다시 선택해주세요.");
                }

                var next = new LobbyOperatingMode(id, mode, reason, at, current.Revision + 1, DateTime.UtcNow);
                modes[id] = next;
                AddModeEvent(
                    id,
                    name,
                    mode == "running" ? "maintenance_off" : mode == "scheduled" ? "shutdown_scheduled" : "maintenance_on",
                    reason
                );
                return new(
                    true,
                    200,
                    mode == "running" ? "운영을 재개했습니다. 점검과 종료 예약을 모두 해제했습니다." : mode == "scheduled" ? "예약 종료를 설정했습니다. 새 입장을 차단합니다." : "입장만 차단하도록 점검을 설정했습니다. 기존 접속은 유지합니다.",
                    next
                );
            }
        }

        // 운영 모드 변경 이벤트를 추가하고 파일 저장 필요 여부를 나타내는 내부 버전을 증가시킵니다.
        private void AddModeEvent(string id, string name, string action, string reason)
        {
            events.Enqueue(new(DateTime.UtcNow, "lobby", id, id, name, action, reason));
            while (events.Count > 200)
            {
                events.Dequeue();
            }

            revision++;
        }

        // 점검과 종료 예약은 같은 운영 상태와 버전을 공유합니다.
        public LobbyMaintenance GetMaintenance(string id)
        {
            var mode = GetOperatingMode(id);
            return new(id, mode.Mode != "running", mode.Reason, mode.ChangedAtUtc, mode.Revision);
        }

        // 통합 운영 모드를 종료 예약 없음·예정·완료 조회 형식으로 바꿉니다.
        public LobbyShutdown GetShutdown(string id)
        {
            var mode = GetOperatingMode(id);
            return new(
                id,
                mode.ShutdownAtUtc,
                mode.Reason,
                mode.Mode == "scheduled" ? "scheduled" : mode.Mode == "maintenance" && mode.ShutdownAtUtc != null ? "completed" : "none",
                mode.Revision,
                mode.ChangedAtUtc
            );
        }

        // 예약 종료 실행 후 점검 모드로 전환합니다. 운영자가 재개하기 전까지 새 입장을 차단합니다.
        public void CompleteShutdown(string id, string name)
        {
            lock (gate)
            {
                if (closing)
                {
                    return;
                }

                var current = GetOperatingMode(id);
                if (current.Mode != "scheduled")
                {
                    return;
                }

                modes[id] = current with
                {
                    Mode = "maintenance",
                    Revision = current.Revision + 1,
                    ChangedAtUtc = DateTime.UtcNow
                };
                AddModeEvent(id, name, "shutdown_completed", current.Reason);
            }
        }
    }
}
