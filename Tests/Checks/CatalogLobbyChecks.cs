using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class CatalogLobbyChecks
{
    private sealed class Processes : ILobbyServerProcessManager
    {
        public readonly Dictionary<string, string> Secrets = new();
        public readonly HashSet<string> Running = new();
        public int Launches;
        public bool Fail;
        public Exception? Failure;
        public void Launch(string id, string executable, string secret)
        { Launches++; if (Failure != null) throw Failure; if (Fail) throw new IOException("fixture launch failure"); Secrets[id] = secret; Running.Add(id); }
        public bool IsRunning(string id) => Running.Contains(id);
        public void Stop(string id) => Running.Remove(id);
    }
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: catalog " + name); }
        var folder = Path.Combine(Path.GetTempPath(), "catalog-lobbies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "catalog.json"), """
                [{"rootContent":"alpha","id":1,"title":"Alpha","children":[]},
                 {"rootContent":"beta","id":2,"title":"Beta","children":[]}]
                """);
            var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
            var process = new Processes();
            var service = new LobbyService(new ContentService(folder), users,
                Options.Create(new LobbyOptions { ServerReadyTimeoutSeconds = 1 }), process);
            service.InitializeCatalogLobbies(); service.InitializeCatalogLobbies();
            Check(service.GetLobbySummaries().Count == 2 && process.Launches == 0, "startup lists two roots without launching processes");
            Check(service.FindById("alpha")!.Name == "Alpha" && service.GetContentsByLobby("alpha")!.Count == 1, "root ID and title define each lobby");
            Check(!service.CreateLobby(new CreateLobbyRequest { LobbyName = "custom" }).Success, "players cannot create catalog lobbies");
            UserInfo User(string name) => users.Login(new LoginRequest { UserName = name }).User!;
            Task<Smart_Training_Server.Responses.Lobby.JoinLobbyResponse> Join(UserInfo user, string id, CancellationToken ct = default) =>
                service.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = id }, ct);
            bool Leave(UserInfo user, string id) => service.LeaveLobby(new LeaveLobbyRequest { UserId = user.Id, LobbyId = id }).Success;
            var a = User("catalog-a"); var b = User("catalog-b"); var c = User("catalog-c");
            var first = Join(a, "alpha"); var second = Join(b, "alpha");
            Check(process.Launches == 1 && service.GetLobbySummary("alpha")!.PendingUsers == 2, "concurrent requests share one starting process");
            Check(users.GetCurrentUser(a.Id)!.PendingLobbyId == "alpha" && a.CurrentLobbyId == "", "pending entry is separate from membership");
            Check(!(await Join(a, "alpha")).Success, "duplicate pending join rejected");
            Check(!service.RegisterLobbyServer("alpha", "wrong", "attacker"), "invalid registration secret rejected");
            Check(service.RegisterLobbyServer("alpha", process.Secrets["alpha"], "alpha-connection"), "worker readiness accepts matching secret");
            Check(!service.RegisterLobbyServer("alpha", process.Secrets["alpha"], "other-connection"), "second worker connection rejected");
            Check((await first).Success && (await second).Success && service.GetLobbySummary("alpha")!.UserCount == 2, "ready worker commits both memberships");
            Check(a.PendingLobbyId == "" && service.GetLobbySummary("alpha")!.PendingUsers == 0, "committed reservations cleared");
            var third = Join(c, "beta");
            Check(process.Running.Count == 2 && process.Launches == 2, "different lobby runs an independent process");
            service.RegisterLobbyServer("beta", process.Secrets["beta"], "beta-connection");
            Check((await third).Success, "second lobby ready entry succeeds");
            var rooms = new RoomService(service, users, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 10, NameMinLength = 1, NameMaxLength = 20 }));
            var room = rooms.CreateRoom(new CreateRoomRequest { UserId = a.Id, LobbyId = "alpha", RoomName = "catalog-room", ContentId = "alpha", MaxUsers = 2 });
            Check(room.Success, "room creation remains available inside catalog lobby");
            Check(!rooms.CreateRoom(new CreateRoomRequest { UserId = a.Id, LobbyId = "alpha", RoomName = "wrong-content", ContentId = "beta", MaxUsers = 2 }).Success, "rooms cannot use another lobby content");
            Check(Leave(a, "alpha") && process.Running.Contains("alpha"), "one departure preserves occupied worker");
            Check(Leave(b, "alpha") && !process.Running.Contains("alpha") && service.FindById("alpha") != null, "last departure stops worker and retains lobby even with empty room metadata");
            Check(service.GetLobbySummary("alpha")!.ServerState == "offline", "idle state exposed in summary");
            using var cancellation = new CancellationTokenSource();
            var canceled = Join(a, "alpha", cancellation.Token);
            cancellation.Cancel();
            Check(!(await canceled).Success && a.PendingLobbyId == "" && !process.Running.Contains("alpha"), "last canceled request stops worker and releases user");
            using var sharedCancellation = new CancellationTokenSource();
            var cancelShared = Join(a, "alpha", sharedCancellation.Token); var remain = Join(b, "alpha");
            sharedCancellation.Cancel();
            Check(!(await cancelShared).Success && process.Running.Contains("alpha"), "canceling one request preserves another pending request");
            service.RegisterLobbyServer("alpha", process.Secrets["alpha"], "shared-connection");
            Check((await remain).Success, "remaining request commits after shared cancellation");
            Check(Leave(b, "alpha"), "remaining participant leaves");
            var pending = Join(a, "alpha");
            Check(Leave(a, "alpha") && !(await pending).Success && a.PendingLobbyId == "", "explicit pending leave invalidates awaiter");
            var timeout = Join(a, "alpha");
            var timedOut = await timeout;
            Check(!timedOut.Success && timedOut.Message.Contains("시간이 초과") && !process.Running.Contains("alpha") && a.PendingLobbyId == "", "readiness timeout has specific message and cleans last reservation");
            process.Fail = true;
            var launchFailed = await Join(a, "alpha");
            Check(!launchFailed.Success && launchFailed.Message.Contains("실행하지 못") && !launchFailed.Message.Contains("fixture") && a.PendingLobbyId == "", "launch failure message omits internal exception and clears pending request");
            process.Fail = false;
            process.Failure = new FileNotFoundException("private fixture error", "private-fixture-path.exe");
            var missing = await Join(a, "alpha");
            Check(!missing.Success && missing.Message.Contains("실행 파일") && !missing.Message.Contains("private") && a.PendingLobbyId == "", "missing executable has distinct message without filesystem details");
            process.Failure = null;
            var lostA = Join(a, "alpha"); var lostB = Join(b, "alpha");
            process.Running.Remove("alpha"); service.MaintainLobbyServers();
            var lostResults = await Task.WhenAll(lostA, lostB);
            Check(lostResults.All(x => !x.Success && x.Message.Contains("연결이 끊겼")) && lostResults[0].Message == lostResults[1].Message && a.PendingLobbyId == "" && b.PendingLobbyId == "", "shared crashed startup preserves failure reason for every waiter after cleanup");
            var retry = Join(a, "alpha");
            service.RegisterLobbyServer("alpha", process.Secrets["alpha"], "retry-ready");
            Check((await retry).Success && a.PendingLobbyId == "", "same lobby accepts fresh attempt after startup failure");
            Leave(a, "alpha");
            Check(!service.RegisterLobbyServer("alpha", process.Secrets["alpha"], "late-worker"), "late registration after canceled or failed launch rejected");
            service.LobbyServerDisconnected("beta", "wrong-connection");
            Check(process.Running.Contains("beta"), "unrelated disconnection preserves occupied worker");
            service.LobbyServerDisconnected("beta", "beta-connection");
            Check(c.CurrentLobbyId == "beta" && service.GetLobbySummary("beta")!.ServerState == "offline", "worker crash preserves established lobby membership");
            await Task.Delay(3100); service.MaintainLobbyServers();
            Check(process.Running.Contains("beta") && service.GetLobbySummary("beta")!.ServerState == "starting", "monitor restarts worker for existing participants");
            service.RegisterLobbyServer("beta", process.Secrets["beta"], "replacement"); Leave(c, "beta");
            Check(process.Running.Count == 0 && service.GetLobbySummaries().Count == 2, "all empty workers stopped while both catalog lobbies remain");
        }
        finally { Directory.Delete(folder, true); }
    }
}
