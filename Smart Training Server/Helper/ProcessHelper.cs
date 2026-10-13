using System.Collections.Concurrent;
using System.Diagnostics;

namespace Smart_Training_Server.Helper;

public static class ProcessHelper
{
    private static readonly ConcurrentDictionary<string, Process> processes = new();
    private static readonly object processGate = new();

    public static void LaunchUnityServer(string id, int port, string executablePath, string? serverToken = null,
        string apiBaseUrl = "http://127.0.0.1:5229")
    {
        lock (processGate)
        {
            try
            {
                string fullPath = Path.GetFullPath(executablePath, AppContext.BaseDirectory);
                if (!File.Exists(fullPath)) throw new FileNotFoundException("Unity 서버 실행 파일을 찾을 수 없습니다.", fullPath);
                if (processes.TryGetValue(id, out var previous))
                {
                    if (!previous.HasExited) throw new InvalidOperationException("해당 방의 서버가 이미 실행 중입니다.");
                    processes.TryRemove(id, out _);
                    previous.Dispose();
                }
                var start = new ProcessStartInfo
                {
                    FileName = fullPath,
                    WorkingDirectory = Path.GetDirectoryName(fullPath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("-Id"); start.ArgumentList.Add(id);
                start.ArgumentList.Add("-port"); start.ArgumentList.Add(port.ToString());
                start.ArgumentList.Add("-batchmode"); start.ArgumentList.Add("-nographics");
                start.ArgumentList.Add("-apiUrl"); start.ArgumentList.Add(apiBaseUrl);
                start.ArgumentList.Add("-logFile"); start.ArgumentList.Add(Path.Combine(start.WorkingDirectory, $"room-{id}.log"));
                if (serverToken != null)
                {
                    // Unity logs command-line arguments. Keep registration secrets out of them.
                    start.Environment["SMART_TRAINING_SERVER_TOKEN"] = serverToken;
                }
                var process = Process.Start(start) ?? throw new InvalidOperationException("프로세스를 시작하지 못했습니다.");
                processes[id] = process;
                try { ProcessJobHelper.AddProcess(process); }
                catch
                {
                    KillProcessById(id);
                    throw;
                }
                Console.WriteLine($"[서버] 실행 완료: {fullPath} (포트 {port}, PID {process.Id})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[서버] 실행 실패 ({id}): {ex.Message}");
                throw;
            }
        }
    }

    public static Process? GetProcessById(string id)
    {
        lock (processGate) return processes.GetValueOrDefault(id);
    }

    public static bool IsProcessRunning(string id)
    {
        lock (processGate)
        {
            if (!processes.TryGetValue(id, out var process)) return false;
            if (!process.HasExited) return true;
            Console.WriteLine($"[서버] 프로세스 종료 감지 ({id}, 종료 코드 {process.ExitCode})");
            return false;
        }
    }

    public static void KillProcessById(string id)
    {
        lock (processGate)
        {
            if (!processes.TryGetValue(id, out var process)) return;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000)) throw new TimeoutException("Unity 프로세스 종료 대기 시간이 초과되었습니다.");
            }
            processes.TryRemove(id, out _);
            process.Dispose();
            Console.WriteLine($"[서버] 프로세스 정리 완료 ({id})");
        }
    }

    public static void KillAll()
    {
        foreach (var id in processes.Keys.ToArray())
        {
            try { KillProcessById(id); }
            catch (Exception ex) { Console.WriteLine($"[서버] 종료 실패 ({id}): {ex.Message}"); }
        }
    }
}
