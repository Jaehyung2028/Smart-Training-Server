using System.Collections.Concurrent;
using System.Diagnostics;

// Unity 전용 서버를 자식 프로세스로 실행하고 ID별로 조회·감시·종료합니다. 로비는 ASP.NET 내부에서 관리하며 Unity 방 서버만 실행합니다.
namespace Smart_Training_Server.Helper
{
    public static class ProcessHelper
    {
        // 한 프로세스의 정보는 한 객체에 모읍니다. 별도의 포트 사전이나 감시 대상 집합은 필요하지 않습니다.
        private sealed class ManagedProcess
        {
            // 생성에 필요한 값을 전달받아 초기화합니다.
            public ManagedProcess(Process process, int port)
            {
                this.Process = process;
                this.Port = port;
            }

            public Process Process
            {
                get;
            }

            public int Port
            {
                get;
            }

            public Task? StopTask
            {
                get; set;
            }
        }

        // 개별 저장소 접근은 ConcurrentDictionary로 보호하고, 여러 단계의 처리는 processGate로 함께 보호합니다.
        private static readonly ConcurrentDictionary<string, ManagedProcess> processes = new();
        // 프로세스 조회·시작·종료 중 Process 객체가 중간에 해제되지 않도록 보호합니다.
        private static readonly object processGate = new();

        // 실행 파일을 확인하고 ID·포트·API 주소를 인자로 전달해 Unity 서버를 시작합니다. 서버 비밀값은 로그 노출을 줄이도록 환경 변수로 전달합니다.
        public static int LaunchUnityServer(
            string id,
            int port,
            string executablePath,
            string? serverToken = null,
            string apiBaseUrl = "http://127.0.0.1:5229"
        )
        {
            lock (processGate)
            {
                bool reservedPort = false;
                try
                {
                    string fullPath = Path.GetFullPath(executablePath, AppContext.BaseDirectory);
                    if (!File.Exists(fullPath))
                    {
                        throw new FileNotFoundException("Unity 서버 실행 파일을 찾을 수 없습니다.", fullPath);
                    }

                    if (processes.TryGetValue(id, out var previous))
                    {
                        if (!previous.Process.HasExited)
                        {
                            throw new InvalidOperationException("해당 방의 서버가 이미 실행 중입니다.");
                        }

                        CleanupExitedProcess(id, previous.Process);
                    }

                    var start = new ProcessStartInfo
                    {
                        FileName = fullPath,
                        WorkingDirectory = Path.GetDirectoryName(fullPath)!,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    start.ArgumentList.Add("-Id");
                    start.ArgumentList.Add(id);
                    // 방 서버는 실행 도구가 포트를 배정하고 종료 시 반환합니다.
                    if (port == 0)
                    {
                        port = PortHelper.GetAvailablePort();
                        reservedPort = true;
                    }

                    start.ArgumentList.Add("-port");
                    start.ArgumentList.Add(port.ToString());
                    start.ArgumentList.Add("-batchmode");
                    start.ArgumentList.Add("-nographics");
                    start.ArgumentList.Add("-apiUrl");
                    start.ArgumentList.Add(apiBaseUrl);
                    start.ArgumentList.Add("-logFile");
                    start.ArgumentList.Add(Path.Combine(start.WorkingDirectory, $"room-{id}.log"));
                    if (serverToken != null)
                    {
                        // Unity가 실행 인자를 로그에 남길 수 있으므로 서버 비밀값은 환경 변수로 전달합니다.
                        start.Environment["SMART_TRAINING_SERVER_TOKEN"] = serverToken;
                    }

                    var process = Process.Start(start) ?? throw new InvalidOperationException("프로세스를 시작하지 못했습니다.");
                    processes[id] = new ManagedProcess(process, port);
                    reservedPort = false; // 이후에는 공통 프로세스 정리 함수가 포트를 반환합니다.
                    try
                    {
                        ProcessJobHelper.AddProcess(process);
                    }
                    catch
                    {
                        RequestStop(id);
                        throw;
                    }

                    Console.WriteLine($"[서버] 실행 완료: {fullPath} (포트 {port}, PID {process.Id})");
                    return port;
                }
                catch (Exception ex)
                {
                    // 프로세스 생성 전 실패한 경우에만 여기서 배정을 취소합니다.
                    if (reservedPort)
                    {
                        PortHelper.ReleasePort(port);
                    }

                    Console.WriteLine($"[서버] 실행 실패 ({id}): {ex.Message}");
                    throw;
                }
            }
        }

        // 관리 ID로 저장된 Process 객체를 조회합니다. 운영체제 PID를 입력하는 함수는 아닙니다.
        public static Process? GetProcessById(string id)
        {
            lock (processGate)
            {
                return processes.GetValueOrDefault(id)?.Process;
            }
        }

        // 튜플로 PID와 실행 여부를 함께 반환합니다. 관리 ID와 운영체제 PID는 서로 다릅니다.
        public static (int? Id, bool Running) GetProcessStatus(string id)
        {
            lock (processGate)
            {
                return processes.TryGetValue(id, out var entry) && !entry.Process.HasExited ? (entry.Process.Id, true) : (null, false);
            }
        }

        // 등록된 프로세스가 아직 살아 있는지 확인합니다. 종료됐으면 종료 코드를 로그에 남깁니다.
        public static bool IsProcessRunning(string id)
        {
            lock (processGate)
            {
                if (!processes.TryGetValue(id, out var entry))
                {
                    return false;
                }

                var process = entry.Process;
                if (!process.HasExited)
                {
                    return true;
                }

                Console.WriteLine($"[서버] 프로세스 종료 감지 ({id}, 종료 코드 {process.ExitCode})");
                return false;
            }
        }

        // 서비스에서는 종료를 요청한 뒤 바로 돌아갑니다. 실제 종료·포트 반환은 비동기로 이어집니다.
        public static void RequestStop(string id)
        {
            var stopping = RequestStopAsync(id);
            _ = ObserveStopAsync(id, stopping);
        }

        private static async Task ObserveStopAsync(string id, Task stopping)
        {
            try
            {
                await stopping;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[서버] 종료 확인 실패 ({id}): {ex.Message}");
            }
        }

        // 잠금에서는 대상 확인과 종료 요청만 하고, 실제 종료를 기다리는 동안에는 잠금을 유지하지 않습니다.
        public static Task RequestStopAsync(string id)
        {
            lock (processGate)
            {
                if (!processes.TryGetValue(id, out var entry))
                {
                    return Task.CompletedTask;
                }

                if (entry.StopTask != null)
                {
                    return entry.StopTask;
                }

                if (!entry.Process.HasExited)
                {
                    entry.Process.Kill(entireProcessTree: true);
                }

                return StartDelayedCleanup(id, entry.Process);
            }
        }

        // 종료 확인이 필요한 도구·검사용 동기 진입점입니다. 서비스의 공통 잠금 안에서는 사용하지 않습니다.
        public static void KillProcessById(string id)
        {
            RequestStopAsync(id).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        // 같은 프로세스의 종료 요청은 하나의 완료 작업을 공유합니다.
        private static Task StartDelayedCleanup(string id, Process process)
        {
            lock (processGate)
            {
                if (!processes.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Process, process))
                {
                    return Task.CompletedTask;
                }

                entry.StopTask ??= CleanupAfterExitAsync(id, process);
                return entry.StopTask;
            }
        }

        private static async Task CleanupAfterExitAsync(string id, Process process)
        {
            try
            {
                var exited = process.WaitForExitAsync();
                try
                {
                    await exited.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    Console.WriteLine($"[서버] 종료가 늦어지고 있습니다 ({id}). 실제 종료 후 정리합니다.");
                    await exited;
                }

                lock (processGate)
                {
                    CleanupExitedProcess(id, process);
                }
            }
            catch (ObjectDisposedException)
            {
                // 다른 정리 경로가 이미 프로세스 리소스를 해제한 경우입니다.
            }
            catch
            {
                lock (processGate)
                {
                    if (processes.TryGetValue(id, out var entry) && ReferenceEquals(entry.Process, process))
                    {
                        entry.StopTask = null;
                    }
                }

                throw;
            }
        }

        // processGate 안에서 호출합니다. 같은 ID의 새 프로세스는 이전 감시 작업이 지우지 못하게 합니다.
        private static void CleanupExitedProcess(string id, Process process)
        {
            if (!processes.TryGetValue(id, out var current) || !ReferenceEquals(current.Process, process))
            {
                return;
            }

            processes.TryRemove(id, out _);
            // 정상 종료·지연 종료 모두 같은 위치에서 포트를 한 번만 반환합니다.
            PortHelper.ReleasePort(current.Port);
            process.Dispose();
            Console.WriteLine($"[서버] 프로세스 정리 완료 ({id})");
        }

        // 현재 도구가 관리하는 모든 프로세스에 종료를 시도합니다. 하나의 실패가 나머지 정리를 막지 않도록 개별 예외를 처리합니다.
        public static void KillAll()
        {
            string[] ids;
            lock (processGate)
            {
                ids = processes.Keys.ToArray();
            }

            // 모두 먼저 종료를 요청하고 한 번에 기다립니다. 방 개수만큼 5초씩 순차 대기하지 않습니다.
            var stopping = new List<Task>();
            foreach (var id in ids)
            {
                try
                {
                    stopping.Add(ObserveStopAsync(id, RequestStopAsync(id)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[서버] 종료 요청 실패 ({id}): {ex.Message}");
                }
            }

            try
            {
                Task.WhenAll(stopping).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                Console.WriteLine("[서버] 일부 프로세스가 종료 중입니다. API 종료 시 Windows Job이 나머지를 정리합니다.");
            }
        }
    }
}
