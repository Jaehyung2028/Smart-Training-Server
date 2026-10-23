using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

// 운영 이력 복원·파일 저장·종료 기록을 담당합니다. 실행 중 메모리 상태와 마지막 저장 상태를 구분합니다.
namespace Smart_Training_Server.Services.Operations
{
    // 운영 이력 저장 사용 여부·마지막 저장 시각·저장 오류를 화면에 전달합니다.
    public sealed record OperationsHistoryStatus(bool Enabled, DateTime? SavedAtUtc, string? Error);

    public sealed partial class ServerOperationsRegistry
    {
        // 파일에 기록하는 운영 이력 형식입니다. 과거 형식의 점검·종료 필드도 복원 호환용으로 남겨 둡니다.
        private sealed record SavedHistory(
            int Version,
            ProcessLifecycle[] Processes,
            ServerOperationEvent[] Events,
            LobbyMaintenance[]? Maintenance = null,
            LobbyShutdown[]? Shutdowns = null,
            LobbyOperatingMode[]? Modes = null
        );

        // 운영 이력 파일 쓰기가 서로 겹치지 않도록 저장 절차를 직렬화하는 잠금입니다.
        private readonly object fileGate = new();
        private readonly string? historyFile;
        private readonly ILogger<ServerOperationsRegistry> logger;
        private long revision, savedRevision = -1;
        private bool closing;
        private DateTime? savedAtUtc;
        private string? historyError;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public ServerOperationsRegistry(string? historyFile = null, ILogger<ServerOperationsRegistry>? logger = null)
        {
            this.logger = logger ?? NullLogger<ServerOperationsRegistry>.Instance;
            this.historyFile = historyFile == null ? null : Path.GetFullPath(historyFile);
            if (this.historyFile != null)
            {
                RestoreHistory();
            }
        }

        // 운영 이력 저장 사용 여부·마지막 저장 시각·저장 오류를 조회합니다.
        public OperationsHistoryStatus HistoryStatus()
        {
            lock (gate)
            {
                return new(historyFile != null, savedAtUtc, historyError);
            }
        }

        // 이전 운영 이력 파일을 검사해 복원합니다. 이전 실행의 starting·running 상태는 현재 살아 있는 프로세스로 간주하지 않습니다.
        private void RestoreHistory()
        {
            try
            {
                if (!File.Exists(historyFile))
                {
                    return;
                }

                if (new FileInfo(historyFile!).Length > 8 * 1024 * 1024)
                {
                    throw new InvalidDataException("History exceeds 8 MB");
                }

                var saved = JsonSerializer.Deserialize<SavedHistory>(File.ReadAllBytes(historyFile!));
                if (saved is not { Version: 1, Processes: not null, Events: not null } ||
                    saved.Processes.Any(x => x == null ||
                    x.Kind is not ("lobby" or "room") ||
                    x.Key != x.Kind + ":" + x.Id) ||
                    saved.Events.Any(x => x == null) ||
                    (saved.Maintenance?.Any(x => x == null ||
                    string.IsNullOrWhiteSpace(x.LobbyId) ||
                    x.Reason == null ||
                    x.Reason.Length > 200 ||
                    x.Revision < 1) ?? false))
                {
                    throw new InvalidDataException("Invalid operations history");
                }

                if (saved.Modes != null)
                {
                    foreach (var mode in saved.Modes)
                    {
                        if (mode == null ||
                            string.IsNullOrWhiteSpace(mode.LobbyId) ||
                            mode.Reason == null ||
                            mode.Reason.Length > 200 ||
                            mode.Revision < 1 ||
                            mode.Mode is not ("running" or "maintenance" or "scheduled") ||
                            (mode.Mode == "scheduled" &&
                            mode.ShutdownAtUtc == null) ||
                            (mode.Mode == "running" &&
                            mode.ShutdownAtUtc != null))
                        {
                            throw new InvalidDataException("Invalid operating mode");
                        }

                        modes.Add(mode.LobbyId, mode);
                    }
                }
                else
                {
                    // 이전 저장 형식의 독립된 설정을 복원할 때 입장 차단과 종료 예약을 보존합니다.
                    var oldMaintenance = (saved.Maintenance ?? []).ToDictionary(x => x.LobbyId);
                    if (saved.Shutdowns?.Any(x => x == null || string.IsNullOrWhiteSpace(x.LobbyId)) == true)
                    {
                        throw new InvalidDataException("Invalid legacy shutdown");
                    }

                    var oldShutdowns = (saved.Shutdowns ?? []).ToDictionary(x => x.LobbyId);
                    foreach (var id in oldMaintenance.Keys.Union(oldShutdowns.Keys))
                    {
                        var m = oldMaintenance.GetValueOrDefault(id);
                        var d = oldShutdowns.GetValueOrDefault(id);
                        if (d != null &&
                            (d.Reason == null ||
                            d.Reason.Length > 200 ||
                            d.Revision < 1 ||
                            d.State is not ("none" or "scheduled" or "completed") ||
                            (d.State != "none" &&
                            d.AtUtc == null)))
                        {
                            throw new InvalidDataException("Invalid legacy shutdown");
                        }

                        bool shutdown = d?.State is "scheduled" or "completed";
                        modes[id] = new(
                            id,
                            d?.State == "scheduled" ? "scheduled" : shutdown || m?.Enabled == true ? "maintenance" : "running",
                            shutdown ? d!.Reason : m?.Reason ?? d?.Reason ?? "",
                            shutdown ? d!.AtUtc : null,
                            Math.Max(1, (m?.Revision ?? 0) + (d?.Revision ?? 0)),
                            shutdown ? d!.ChangedAtUtc : m?.ChangedAtUtc ?? d?.ChangedAtUtc
                        );
                    }

                    if (modes.Count > 0)
                    {
                        revision++;
                    }
                }

                var restored = saved.Processes.ToDictionary(x => x.Key);
                foreach (var item in restored.Values.Where(x => x.State is "offline" or "failed").OrderByDescending(x => x.UpdatedAtUtc).Take(512))
                {
                    processes[item.Key] = item;
                }

                foreach (var item in saved.Events.TakeLast(200))
                {
                    events.Enqueue(item);
                }

                var now = DateTime.UtcNow;
                foreach (var item in restored.Values.Where(x => x.State is "starting" or "running"))
                {
                    const string reason = "이전 ASP.NET 실행이 정상 종료되지 않아 실행 상태를 초기화했습니다.";
                    processes[item.Key] = item with
                    {
                        State = "offline",
                        StoppedAtUtc = now,
                        FailedAtUtc = now,
                        LastFailure = reason,
                        UpdatedAtUtc = now
                    };
                    events.Enqueue(new(now, item.Kind, item.Id, item.LobbyId, item.Name, "interrupted", reason));
                    revision++;
                }

                while (events.Count > 200)
                {
                    events.Dequeue();
                }

                foreach (var old in processes.Values.OrderByDescending(x => x.UpdatedAtUtc).Skip(512).ToArray())
                {
                    processes.Remove(old.Key);
                }

                savedAtUtc = File.GetLastWriteTimeUtc(historyFile!);
                savedRevision = revision == 0 ? 0 : -1;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            {
                processes.Clear();
                events.Clear();
                modes.Clear();
                historyError = "이전 운영 기록을 불러오지 못했습니다. 서버 로그를 확인해주세요.";
                logger.LogWarning(error, "Operations history restore failed");
                try
                {
                    File.Copy(historyFile!, historyFile + ".invalid-" + DateTime.UtcNow.Ticks, false);
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(backupError, "Operations history invalid-file preservation failed");
                }
            }
        }

        // 변경된 운영 기록을 임시 파일에 기록한 뒤 본 파일로 교체합니다. 실패하면 메모리 기록을 유지하고 재시도할 수 있게 합니다.
        public void FlushHistory()
        {
            if (historyFile == null)
            {
                return;
            }

            lock (fileGate)
            {
                SavedHistory data;
                long version;
                lock (gate)
                {
                    if (savedRevision == revision && File.Exists(historyFile))
                    {
                        return;
                    }

                    version = revision;
                    data = new(1, processes.Values.ToArray(), events.ToArray(), null, null, modes.Values.ToArray());
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(historyFile)!);
                    // 임시 파일에 기록을 완성한 뒤 본 파일로 교체하여 저장 도중 파일 손상 가능성을 줄입니다.
                    string temporary = historyFile + ".tmp";
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data);
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        output.Write(bytes);
                        output.Flush(true);
                    }

                    File.Move(temporary, historyFile, true);
                    lock (gate)
                    {
                        savedRevision = version;
                        savedAtUtc = DateTime.UtcNow;
                        historyError = null;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    lock (gate)
                    {
                        if (historyError == null)
                        {
                            logger.LogWarning(error, "Operations history save failed; retaining in-memory history");
                        }

                        historyError = "운영 기록 파일 저장에 실패했습니다. 현재 기록은 메모리에 유지하며 저장을 다시 시도합니다.";
                    }
                }
            }
        }

        // 종료 시 아직 실행 상태인 기록을 종료로 바꾸고 마지막 파일 저장을 수행합니다.
        public void CompleteSession()
        {
            lock (gate)
            {
                foreach (var item in processes.Values.Where(x => x.State is "starting" or "running").ToArray())
                {
                    Record(item.Kind, item.Id, item.LobbyId, item.Name, "stopped", "ASP.NET 서버 정상 종료");
                }

                closing = true;
            }

            FlushHistory();
        }
    }

    public sealed class OperationsHistoryWriter : BackgroundService
    {
        private readonly ServerOperationsRegistry registry;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public OperationsHistoryWriter(ServerOperationsRegistry registry)
        {
            this.registry = registry;
        }

        // 운영 이력을 주기적으로 파일에 반영합니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            registry.FlushHistory();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    registry.FlushHistory();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        // 호스트 종료 시 작업과 등록된 자원을 정리합니다. 이력 작성 서비스는 마지막 운영 기록도 저장합니다.
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await base.StopAsync(cancellationToken);
            }
            finally
            {
                registry.CompleteSession();
            }
        }
    }
}
