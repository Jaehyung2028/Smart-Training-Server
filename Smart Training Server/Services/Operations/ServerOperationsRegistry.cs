// 프로세스 생명주기와 최근 운영 이벤트를 보관합니다. 사용자 인증 데이터와 분리된 운영 기록 저장소입니다.
namespace Smart_Training_Server.Services.Operations
{
    // 프로세스별 마지막 상태와 시작·종료·실패 시각, 실행 횟수를 묶는 불변 운영 기록입니다.
    public sealed record ProcessLifecycle(
        string Key,
        string Kind,
        string Id,
        string LobbyId,
        string Name,
        string State,
        DateTime? StartedAtUtc,
        DateTime? StoppedAtUtc,
        DateTime? FailedAtUtc,
        string? LastFailure,
        int StartCount,
        DateTime UpdatedAtUtc
    );

    // 운영 이벤트 한 건입니다. 시각·대상·동작·사유를 보관하며 최근 기록 큐에 넣습니다.
    public sealed record ServerOperationEvent(
        DateTime AtUtc,
        string Kind,
        string Id,
        string LobbyId,
        string Name,
        string Action,
        string? Reason
    );

    // 운영 메타데이터만 저장하며 사용자·인증 정보는 포함하지 않습니다.
    public sealed partial class ServerOperationsRegistry
    {
        // 운영 상태와 이벤트를 함께 바꿀 때 일관성을 지키는 잠금입니다.
        private readonly object gate = new();
        // 관리 ID별 저장소입니다. 이 클래스의 값 타입에 따라 실제 프로세스와 운영 이력 정보를 구분합니다.
        private readonly Dictionary<string, ProcessLifecycle> processes = new();
        // 최근 이벤트를 순서대로 보관하는 큐입니다. 보관 한도를 넘으면 오래된 항목을 앞에서 제거합니다.
        private readonly Queue<ServerOperationEvent> events = new();

        // 현재 API 실행의 시작 시각입니다. 운영 화면의 가동 시간 계산에 사용합니다.
        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        // 프로세스별 마지막 상태와 최근 이벤트를 갱신합니다. 중복 종료·준비 이벤트를 줄이고 보관 개수를 제한합니다.
        public void Record(
            string kind,
            string id,
            string lobbyId,
            string name,
            string action,
            string? reason = null
        )
        {
            lock (gate)
            {
                if (closing)
                {
                    return;
                }

                string key = kind + ":" + id;
                processes.TryGetValue(key, out var previous);
                // 반복 정리로 동일한 종료 이력이 중복 기록되지 않게 합니다.
                if (action == "stopped" && (previous == null || previous.State is "offline" or "failed"))
                {
                    return;
                }

                if (action == "ready" && previous?.State == "running")
                {
                    return;
                }

                var now = DateTime.UtcNow;
                processes[key] = new(key, kind, id, lobbyId, name, action switch
                {
                    "starting" => "starting",
                    "ready" => "running",
                    "failed" => "failed",
                    _ => "offline"
                }, action == "starting" ? now : previous?.StartedAtUtc, action is "stopped" or "failed" ? now : previous?.StoppedAtUtc, action == "failed" ? now : previous?.FailedAtUtc, action == "failed" ? reason : previous?.LastFailure, (previous?.StartCount ?? 0) + (action == "starting" ? 1 : 0), now);
                events.Enqueue(new(now, kind, id, lobbyId, name, action, reason));
                revision++;
                while (events.Count > 200)
                {
                    events.Dequeue();
                }

                foreach (var old in processes.Values.Where(x => x.State is "offline" or "failed").OrderByDescending(x => x.UpdatedAtUtc).Skip(512).ToArray())
                {
                    processes.Remove(old.Key);
                }

                logger.LogInformation("운영 이벤트: {Kind}, 로비: {LobbyId}, 대상: {Id}, 동작: {Action}, 시각: {AtUtc}, 사유: {Reason}", kind, lobbyId, id, action, now, reason);
            }
        }

        // 프로세스 상태와 최근 이벤트를 배열로 복사해 반환합니다. 조회 중에만 내부 잠금을 잡습니다.
        public (IReadOnlyList<ProcessLifecycle> Processes, IReadOnlyList<ServerOperationEvent> Events) Snapshot()
        {
            lock (gate)
            {
                return (processes.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(), events.Reverse().ToArray());
            }
        }
    }
}
