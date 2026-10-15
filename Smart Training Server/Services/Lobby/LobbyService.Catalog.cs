using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Lobby;

namespace Smart_Training_Server.Services.Lobby;

public partial class LobbyService
{
    private const string LobbyCanceled = "로비 입장을 취소했습니다.";
    private const string LobbyTimedOut = "로비 서버 준비 시간이 초과되었습니다. 잠시 후 같은 로비에 다시 입장해주세요.";
    private const string LobbyConnectionLost = "로비 서버가 준비 중에 종료되었거나 연결이 끊겼습니다. 같은 로비에 다시 입장해주세요.";
    private sealed record StartupResult(bool Ready, string Message = "");
    private bool catalogInitialized;
    private readonly ILobbyServerProcessManager lobbyProcesses;
    private readonly Dictionary<string, Runtime> runtimes = new();
    private readonly Dictionary<string, PendingJoin> pendingJoins = new();
    private sealed class PendingJoin(string userId, string lobbyId)
    { public string UserId = userId; public string LobbyId = lobbyId; }
    private sealed class Runtime(string executable)
    {
        public string Executable = executable, State = "offline", Secret = "", Connection = "";
        public DateTime RetryAfter;
        public DateTime StartedAt;
        public TaskCompletionSource<StartupResult> Ready = NewReady();
    }
    private static TaskCompletionSource<StartupResult> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void InitializeCatalogLobbies()
    {
        var contents = contentService.LoadLobbyContents();
        if (lobbyOptions.ServerReadyTimeoutSeconds is < 1 or > 300 || string.IsNullOrWhiteSpace(lobbyOptions.ServerExecutablePath))
            throw new InvalidDataException("로비 서버 실행 파일과 1~300초 준비 시간을 지정해주세요.");
        foreach (var content in contents.Values)
            if (!Regex.IsMatch(content.RootContent, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
                throw new InvalidDataException("catalog.json의 rootContent는 영문·숫자·점·밑줄·하이픈의 1~64자 ID여야 합니다.");
        lock (userService.MembershipLock)
        {
            if (catalogInitialized) return;
            if (!lobbies.IsEmpty) throw new InvalidOperationException("로비 사용 전에 카탈로그를 초기화해주세요.");
            foreach (var content in contents.Values.OrderBy(x => x.RootContent, StringComparer.Ordinal))
            {
                var lobby = CreateLobbyInfo(content.Title);
                lobby.Id = content.RootContent; lobby.IsCatalogLobby = true;
                lobby.Contents[content.RootContent] = content;
                lobbies[lobby.Id] = lobby;
                runtimes[lobby.Id] = new Runtime(string.IsNullOrWhiteSpace(content.LobbyServerExecutablePath)
                    ? lobbyOptions.ServerExecutablePath : content.LobbyServerExecutablePath);
            }
            catalogInitialized = true;
        }
    }
    private async Task<JoinLobbyResponse> JoinCatalogLobby(JoinLobbyRequest request, CancellationToken cancellation)
    {
        PendingJoin pending;
        Task<StartupResult> readiness;
        LobbyInfo lobby;
        lock (userService.MembershipLock)
        {
            var user = userService.FindById(request.UserId);
            var found = FindById(request.LobbyId);
            if (cancellation.IsCancellationRequested) return Error("로비 입장을 취소했습니다.");
            if (user == null || found == null) return Error("사용자 또는 로비를 찾을 수 없습니다.");
            if (!string.IsNullOrEmpty(user.CurrentLobbyId) || pendingJoins.ContainsKey(user.Id)) return Error("이미 로비에 입장했거나 입장을 요청했습니다.");
            var shutdown = operations.GetShutdown(found.Id);
            if (shutdown.State != "none") return Error("종료 예정이거나 종료된 로비입니다. " + shutdown.Reason);
            var maintenance = operations.GetMaintenance(found.Id);
            if (maintenance.Enabled) return Error("점검 중인 로비입니다. " + maintenance.Reason);
            lobby = found;
            pending = new(user.Id, lobby.Id); pendingJoins.Add(user.Id, pending); user.PendingLobbyId = lobby.Id;
            StartLobby(lobby);
            readiness = runtimes[lobby.Id].Ready.Task;
        }
        StartupResult outcome;
        try { outcome = await readiness.WaitAsync(TimeSpan.FromSeconds(lobbyOptions.ServerReadyTimeoutSeconds), cancellation); }
        catch (OperationCanceledException) { outcome = new(false, LobbyCanceled); }
        catch (TimeoutException) { outcome = new(false, LobbyTimedOut); }
        lock (userService.MembershipLock)
        {
            if (!pendingJoins.TryGetValue(pending.UserId, out var current) || current != pending) return Error("로비 입장 요청이 취소되었습니다.");
            var user = userService.FindById(pending.UserId);
            var runtime = runtimes[lobby.Id];
            if (!outcome.Ready || cancellation.IsCancellationRequested || user == null || runtime.State != "running")
            {
                if (!outcome.Ready && !cancellation.IsCancellationRequested && outcome.Message == LobbyTimedOut &&
                    runtime.Ready.Task == readiness && runtime.State == "starting" && lobby.Users.IsEmpty &&
                    pendingJoins.Values.Count(x => x.LobbyId == lobby.Id) == 1)
                    ResetLobby(lobby, "failed", LobbyTimedOut);
                ReleasePendingJoin(pending);
                return Error(cancellation.IsCancellationRequested ? LobbyCanceled :
                    outcome.Message.Length > 0 ? outcome.Message : LobbyConnectionLost);
            }
            lobby.Users[user.Id] = user;
            user.CurrentLobbyId = lobby.Id; user.CurrentLobbyName = lobby.Name;
            ReleasePendingJoin(pending); lobby.LastActiveTime = DateTime.UtcNow;
            return new() { Success = true, Message = "로비에 입장했습니다.", Lobby = lobby };
        }
    }
    private static JoinLobbyResponse Error(string message) => new() { Success = false, Message = message };
    private void ReleasePendingJoin(PendingJoin pending)
    {
        pendingJoins.Remove(pending.UserId);
        var user = userService.FindById(pending.UserId);
        if (user?.PendingLobbyId == pending.LobbyId) user.PendingLobbyId = "";
        var lobby = FindById(pending.LobbyId);
        if (lobby != null) StopIdleLobby(lobby);
    }
    private void StartLobby(LobbyInfo lobby)
    {
        var runtime = runtimes[lobby.Id];
        if (runtime.State is "starting" or "running") return;
        runtime.Ready = NewReady(); runtime.Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        runtime.Connection = ""; runtime.State = "starting";
        runtime.StartedAt = DateTime.UtcNow;
        operations.Record("lobby", lobby.Id, lobby.Id, lobby.Name, "starting");
        try { lobbyProcesses.Launch(lobby.Id, runtime.Executable, runtime.Secret); }
        catch (Exception ex)
        {
            runtime.State = "failed"; runtime.Secret = ""; runtime.RetryAfter = DateTime.UtcNow.AddSeconds(3);
            string message = ex is FileNotFoundException or DirectoryNotFoundException
                ? "로비 서버 실행 파일을 찾지 못했습니다. 서버 관리자에게 실행 파일 설정 확인을 요청해주세요."
                : "로비 서버를 실행하지 못했습니다. 잠시 후 다시 입장해주세요. 반복되면 서버 관리자에게 문의해주세요.";
            operations.Record("lobby", lobby.Id, lobby.Id, lobby.Name, "failed", message);
            runtime.Ready.TrySetResult(new(false, message)); Console.WriteLine($"[Lobby] 실행 실패 {lobby.Id}: {ex.Message}");
        }
    }
    private void StopIdleLobby(LobbyInfo lobby)
    {
        if (!lobby.IsCatalogLobby || !lobby.Users.IsEmpty || pendingJoins.Values.Any(x => x.LobbyId == lobby.Id)) return;
        ResetLobby(lobby, "offline");
    }
    private void ResetLobby(LobbyInfo lobby, string state, string failureMessage = LobbyCanceled)
    {
        var runtime = runtimes[lobby.Id];
        runtime.Secret = ""; runtime.Connection = ""; runtime.State = state;
        runtime.Ready.TrySetResult(new(false, failureMessage)); runtime.RetryAfter = DateTime.UtcNow.AddSeconds(3);
        lobbyProcesses.Stop(lobby.Id);
        operations.Record("lobby", lobby.Id, lobby.Id, lobby.Name,
            failureMessage == LobbyCanceled ? "stopped" : "failed",
            failureMessage == LobbyCanceled ? "접속·입장 대기 인원 없음" : failureMessage);
    }
    public bool RegisterLobbyServer(string lobbyId, string secret, string connection)
    {
        lock (userService.MembershipLock)
        {
            if (!runtimes.TryGetValue(lobbyId, out var runtime) || runtime.State is not ("starting" or "running") ||
                string.IsNullOrEmpty(runtime.Secret) || runtime.Secret != secret ||
                (runtime.Connection != "" && runtime.Connection != connection)) return false;
            runtime.Connection = connection; runtime.State = "running";
            operations.Record("lobby", lobbyId, lobbyId, lobbies[lobbyId].Name, "ready");
            runtime.Ready.TrySetResult(new(true)); return true;
        }
    }
    public void LobbyServerDisconnected(string lobbyId, string connection)
    {
        lock (userService.MembershipLock)
            if (runtimes.TryGetValue(lobbyId, out var runtime) && runtime.Connection == connection)
                ResetLobby(lobbies[lobbyId], "offline", LobbyConnectionLost);
    }
    public void MaintainLobbyServers()
    {
        lock (userService.MembershipLock)
        {
            foreach (var lobby in lobbies.Values.Where(x => x.IsCatalogLobby))
            {
                var runtime = runtimes[lobby.Id];
                bool needed = !lobby.Users.IsEmpty || pendingJoins.Values.Any(x => x.LobbyId == lobby.Id);
                if (!needed) { if (runtime.State != "offline") StopIdleLobby(lobby); continue; }
                if (runtime.State is "starting" or "running" && !lobbyProcesses.IsRunning(lobby.Id)) ResetLobby(lobby, "offline", LobbyConnectionLost);
                if (runtime.State == "starting" && DateTime.UtcNow - runtime.StartedAt >= TimeSpan.FromSeconds(lobbyOptions.ServerReadyTimeoutSeconds)) ResetLobby(lobby, "failed", LobbyTimedOut);
                if (runtime.State is "offline" or "failed" && DateTime.UtcNow >= runtime.RetryAfter) StartLobby(lobby);
            }
        }
    }
}
