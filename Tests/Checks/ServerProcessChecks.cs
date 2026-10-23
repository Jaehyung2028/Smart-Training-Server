using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class ServerProcessChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        void CheckPortReleased(int port, RoomInfo room, string name)
        {
            var nextPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
            try
            {
                Check(nextPort == port && room.Port == 0, name);
            }
            finally
            {
                Smart_Training_Server.Helper.PortHelper.ReleasePort(nextPort);
            }
        }

        var manager = new TestProcesses();
        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.Create(users, "server-lobby");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    MinUsers = 1,
                    MaxUsers = 10,
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    AdvertisedAddress = "192.168.1.20",
                    ServerExecutablePath = "configured-server.exe",
                    ServerReadyTimeoutSeconds = 1
                }),
            processManager: manager
        );
        var a = users.Login(new LoginRequest { UserName = "server-a" }).User!;
        var b = users.Login(new LoginRequest { UserName = "server-b" }).User!;
        var lobby = TestCatalogLobbies.Get(lobbies, "server-lobby");
        await lobbies.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        await lobbies.JoinLobby(b.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        var room = TestRoomSetup.Create(rooms,
            a.Id,
            new CreateRoomRequest {
                LobbyId = lobby.Id,
                RoomName = "server-room",
                ContentId = "sample-training",
                MaxUsers = 2
            }
        ).Room!;
        Task<Smart_Training_Server.Responses.Room.JoinRoomResponse> Join(UserInfo user)
        {
            if (rooms.FindById(room.Id) == null)
            {
                room = TestRoomSetup.Create(rooms, a.Id, new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "server-room", ContentId = "sample-training", MaxUsers = 2 }).Room!;
            }

            return rooms.JoinRoom(user.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
        }
        manager.FailLaunch = true;
        Check(
            !(await Join(a)).Success && rooms.FindById(room.Id) == null && room.ServerToken == "" && room.ReservedUsers == 0 && a.PendingRoomId == "",
            "launch failure clears room and reservation"
        );
        CheckPortReleased(manager.LastPort, room, "launch failure releases allocated port");
        manager.FailLaunch = false;
        var timeoutA = Join(a);
        var timeoutB = Join(b);
        var expiredToken = room.ServerToken;
        Check(manager.LastPath == "configured-server.exe", "use configured executable path");
        var timeouts = await Task.WhenAll(timeoutA, timeoutB).WaitAsync(TimeSpan.FromSeconds(3));
        Check(
            timeouts.All(x => !x.Success) && room.ReservedUsers == 0 && a.PendingRoomId == "" && b.PendingRoomId == "" && !manager.IsRunning(room.Id),
            "ready timeout releases all concurrent reservations and stops server"
        );
        CheckPortReleased(manager.LastPort, room, "readiness timeout releases allocated port");
        Check(!rooms.RegisterServer(room.Id, expiredToken, "late"), "reject late readiness from timed-out launch");
        var waiting = Join(a);
        manager.Crash(room.Id);
        rooms.CheckServerProcesses();
        Check(
            !(await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Success && rooms.FindById(room.Id) == null && room.ReservedUsers == 0,
            "detect crash before READY and release waiter"
        );
        var retry = Join(a);
        var activeToken = room.ServerToken;
        Check(rooms.RegisterServer(room.Id, activeToken, "active"), "retry launches and registers a new server");
        var admission = await retry;
        Check(
            room.IpAddress == "192.168.1.20" && admission.Room!.IpAddress == "192.168.1.20",
            "configured advertised address reaches room connection response"
        );
        Check(
            admission.Success && rooms.ConfirmConnection(room.Id, "active", a.Id, admission.AdmissionToken).Success,
            "retry establishes user connection"
        );
        var bAdmission = await Join(b);
        Check(bAdmission.Success && manager.Launches == 3, "running server reused for another reservation");
        room.IsRunning = true;
        room.PhaseRevision = 10;
        manager.Crash(room.Id);
        rooms.CheckServerProcesses();
        Check(
            room.Users.IsEmpty && room.ReservedUsers == 0 && a.CurrentRoomId == "" && b.PendingRoomId == "" && rooms.FindById(room.Id) == null && room.ServerToken == "",
            "detect running crash and clean connected and reserved users"
        );
        CheckPortReleased(manager.LastPort, room, "process crash releases allocated port");
        Check(a.CurrentLobbyId == lobby.Id && b.CurrentLobbyId == lobby.Id, "server crash preserves lobby membership");
        Check(
            !room.IsRunning && room.PhaseRevision == 11 && string.IsNullOrEmpty(room.MasterUserId),
            "process crash resets running phase and master with a new revision"
        );
        Check(
            !rooms.ConfirmConnection(room.Id, "active", b.Id, bAdmission.AdmissionToken).Success,
            "old server cannot confirm after crash"
        );
        rooms.CheckServerProcesses();
        var newWait = Join(a);
        var newToken = room.ServerToken;
        rooms.ServerDisconnected(room.Id, "active");
        Check(
            room.State == ServerState.Starting && room.ServerToken == newToken,
            "late old SignalR disconnect cannot reset new launch"
        );
        Check(rooms.RegisterServer(room.Id, newToken, "replacement"), "replacement registers after crash");
        Check((await newWait).Success, "replacement returns fresh connection details");
        rooms.ServerDisconnected(room.Id, "replacement");
        Check(
            !manager.IsRunning(room.Id) && room.ReservedUsers == 0,
            "SignalR disconnect stops process and clears reservations"
        );
        CheckPortReleased(manager.LastPort, room, "SignalR shutdown releases allocated port");
        try
        {
            Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer("missing-file-test", 7777, "does-not-exist.exe");
            throw new Exception("missing file accepted");
        }
        catch (FileNotFoundException)
        {
            Check(true, "actual process helper rejects missing executable immediately");
        }

        var fixture = Path.GetFullPath("Tests/ProcessFixture/bin/Debug/net10.0/ProcessFixture.exe");
        // 실제 Kill은 보통 즉시 끝나므로 시간 초과 후 감시 시작 지점을 직접 호출합니다.
        // 자식 프로세스가 자연 종료될 때 기록과 포트를 자동 정리하는지 확인합니다.
        const string delayedId = "delayed-exit-fixture";
        var delayedPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
        try
        {
            Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer(delayedId, delayedPort, fixture);
            var delayedProcess = Smart_Training_Server.Helper.ProcessHelper.GetProcessById(delayedId)!;
            var startCleanup = typeof(Smart_Training_Server.Helper.ProcessHelper).GetMethod(
                "StartDelayedCleanup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var firstStop = (Task)startCleanup.Invoke(null, new object[] { delayedId, delayedProcess })!;
            var repeatedStop = (Task)startCleanup.Invoke(null, new object[] { delayedId, delayedProcess })!;
            Check(ReferenceEquals(firstStop, repeatedStop), "repeated stop requests share one cleanup task");
            var statusQuery = Task.Run(() => Smart_Training_Server.Helper.ProcessHelper.GetProcessStatus(delayedId));
            Check(await Task.WhenAny(statusQuery, firstStop) == statusQuery, "process status query runs while another process exit is pending");

            var heldPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
            try
            {
                Check(heldPort != delayedPort, "timed-out live process keeps its port reserved");
            }
            finally
            {
                Smart_Training_Server.Helper.PortHelper.ReleasePort(heldPort);
            }

            var cleanupDeadline = DateTime.UtcNow.AddSeconds(5);
            while (Smart_Training_Server.Helper.ProcessHelper.GetProcessById(delayedId) != null && DateTime.UtcNow < cleanupDeadline)
            {
                await Task.Delay(25);
            }

            Check(Smart_Training_Server.Helper.ProcessHelper.GetProcessById(delayedId) == null, "late exit automatically removes process record without another stop request");
            var reclaimedPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
            try
            {
                Check(reclaimedPort == delayedPort, "late exit automatically releases reserved port");
                await Task.Delay(50);
                var nextPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
                Check(nextPort != reclaimedPort, "completed cleanup does not release a newly allocated port twice");
                Smart_Training_Server.Helper.PortHelper.ReleasePort(nextPort);
            }
            finally
            {
                Smart_Training_Server.Helper.PortHelper.ReleasePort(reclaimedPort);
            }
        }
        finally
        {
            Smart_Training_Server.Helper.ProcessHelper.KillProcessById(delayedId);
        }

        // 실제 실행 도구가 배정과 정상 종료 반환을 모두 담당하는지 확인합니다.
        var expectedPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
        Smart_Training_Server.Helper.PortHelper.ReleasePort(expectedPort);
        var autoPort = Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer("auto-port-fixture", 0, fixture);
        Check(autoPort == expectedPort, "process launch allocates room port");
        Smart_Training_Server.Helper.ProcessHelper.KillProcessById("auto-port-fixture");
        var afterStop = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
        Check(afterStop == autoPort, "normal process cleanup releases port without RoomService");
        Smart_Training_Server.Helper.PortHelper.ReleasePort(afterStop);

        // 파일은 존재하지만 실행할 수 없는 경우에도 미리 배정한 포트를 반환합니다.
        var invalidExecutable = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        File.WriteAllText(invalidExecutable, "not an executable");
        try
        {
            try
            {
                Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer("invalid-executable", 0, invalidExecutable);
                throw new Exception("invalid executable accepted");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                var afterFailure = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
                Check(afterFailure == expectedPort, "process start failure rolls back port allocation");
                Smart_Training_Server.Helper.PortHelper.ReleasePort(afterFailure);
            }
        }
        finally
        {
            File.Delete(invalidExecutable);
        }

        const string fixtureId = "process-fixture-test";
        try
        {
            Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer(fixtureId, 7777, fixture, "fixture-secret");
            Check(
                Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId),
                "actual process starts hidden and is tracked"
            );
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Check(!Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId), "actual child exit is detected");
        }
        finally
        {
            Smart_Training_Server.Helper.ProcessHelper.KillProcessById(fixtureId);
        }

        Check(
            Smart_Training_Server.Helper.ProcessHelper.GetProcessById(fixtureId) == null,
            "actual exited process handle is released"
        );
    }

    private sealed class TestProcesses : IRoomServerProcessManager
    {
        private readonly HashSet<string> running = new();
        private readonly Dictionary<string, int> ports = new();

        public bool FailLaunch { get; set; }

        public string LastPath { get; private set; } = "";

        public int Launches { get; private set; }

        public int LastPort { get; private set; }

        public int Launch(string roomId, string executablePath, string serverToken)
        {
            LastPath = executablePath;
            LastPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
            if (FailLaunch)
            {
                Smart_Training_Server.Helper.PortHelper.ReleasePort(LastPort);
                throw new IOException("simulated launch failure");
            }

            Launches++;
            running.Add(roomId);
            ports[roomId] = LastPort;
            return LastPort;
        }

        public bool IsRunning(string id) => running.Contains(id);

        public void Stop(string id)
        {
            running.Remove(id);
            if (ports.Remove(id, out var port)) Smart_Training_Server.Helper.PortHelper.ReleasePort(port);
        }

        public void Crash(string id) => running.Remove(id);
    }
}
