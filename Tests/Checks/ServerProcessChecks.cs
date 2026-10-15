using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class ServerProcessChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var manager = new TestProcesses();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { MaxCount = 10, NameMinLength = 1, NameMaxLength = 20 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 10, NameMinLength = 1, NameMaxLength = 20, AdvertisedAddress = "192.168.1.20", ServerExecutablePath = "configured-server.exe", ServerReadyTimeoutSeconds = 1 }), processManager: manager);
        var a = users.Login(new LoginRequest { UserName = "server-a" }).User!;
        var b = users.Login(new LoginRequest { UserName = "server-b" }).User!;
        var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "server-lobby" }).Lobby!;
        await lobbies.JoinLobby(new JoinLobbyRequest { UserId = a.Id, LobbyId = lobby.Id });
        await lobbies.JoinLobby(new JoinLobbyRequest { UserId = b.Id, LobbyId = lobby.Id });
        var room = rooms.CreateRoom(new CreateRoomRequest { UserId = a.Id, LobbyId = lobby.Id, RoomName = "server-room", ContentId = "sample-training", MaxUsers = 2 }).Room!;
        Task<Smart_Training_Server.Responses.Room.JoinRoomResponse> Join(UserInfo user) => rooms.JoinRoom(new JoinRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomId = room.Id });
        manager.FailLaunch = true;
        Check(!(await Join(a)).Success && room.State == ServerState.Creating && room.ServerToken == "" && room.ReservedUsers == 0 && a.PendingRoomId == "", "launch failure clears room and reservation");
        manager.FailLaunch = false;
        var timeoutA = Join(a); var timeoutB = Join(b);
        var expiredToken = room.ServerToken;
        Check(manager.LastPath == "configured-server.exe", "use configured executable path");
        var timeouts = await Task.WhenAll(timeoutA, timeoutB).WaitAsync(TimeSpan.FromSeconds(3));
        Check(timeouts.All(x => !x.Success) && room.ReservedUsers == 0 && a.PendingRoomId == "" && b.PendingRoomId == "" && !manager.IsRunning(room.Id), "ready timeout releases all concurrent reservations and stops server");
        Check(!rooms.RegisterServer(room.Id, expiredToken, "late"), "reject late readiness from timed-out launch");
        var waiting = Join(a);
        manager.Crash(room.Id);
        rooms.CheckServerProcesses();
        Check(!(await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Success && room.State == ServerState.Creating && room.ReservedUsers == 0, "detect crash before READY and release waiter");
        var retry = Join(a); var activeToken = room.ServerToken;
        Check(rooms.RegisterServer(room.Id, activeToken, "active"), "retry launches and registers a new server");
        var admission = await retry;
        Check(room.IpAddress == "192.168.1.20" && admission.Room!.IpAddress == "192.168.1.20", "configured advertised address reaches room connection response");
        Check(admission.Success && rooms.ConfirmConnection(room.Id, "active", a.Id, admission.AdmissionToken).Success, "retry establishes user connection");
        var bAdmission = await Join(b);
        Check(bAdmission.Success && manager.Launches == 3, "running server reused for another reservation");
        room.IsRunning = true; room.PhaseRevision = 10;
        manager.Crash(room.Id);
        rooms.CheckServerProcesses();
        Check(room.Users.IsEmpty && room.ReservedUsers == 0 && a.CurrentRoomId == "" && b.PendingRoomId == "" && room.State == ServerState.Creating && room.ServerToken == "", "detect running crash and clean connected and reserved users");
        Check(a.CurrentLobbyId == lobby.Id && b.CurrentLobbyId == lobby.Id, "server crash preserves lobby membership");
        Check(!room.IsRunning && room.PhaseRevision == 11 && string.IsNullOrEmpty(room.MasterUserId),
            "process crash resets running phase and master with a new revision");
        Check(!rooms.ConfirmConnection(room.Id, "active", b.Id, bAdmission.AdmissionToken).Success, "old server cannot confirm after crash");
        rooms.CheckServerProcesses();
        var newWait = Join(a); var newToken = room.ServerToken;
        rooms.ServerDisconnected(room.Id, "active");
        Check(room.State == ServerState.Starting && room.ServerToken == newToken, "late old SignalR disconnect cannot reset new launch");
        Check(rooms.RegisterServer(room.Id, newToken, "replacement"), "replacement registers after crash");
        Check((await newWait).Success, "replacement returns fresh connection details");
        rooms.ServerDisconnected(room.Id, "replacement");
        Check(!manager.IsRunning(room.Id) && room.ReservedUsers == 0, "SignalR disconnect stops process and clears reservations");
        try
        {
            Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer("missing-file-test", 7777, "does-not-exist.exe");
            throw new Exception("missing file accepted");
        }
        catch (FileNotFoundException) { Check(true, "actual process helper rejects missing executable immediately"); }
        var fixture = Path.GetFullPath("Tests/ProcessFixture/bin/Debug/net10.0/ProcessFixture.exe");
        const string fixtureId = "process-fixture-test";
        try
        {
            Smart_Training_Server.Helper.ProcessHelper.LaunchUnityServer(fixtureId, 7777, fixture, "fixture-secret");
            Check(Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId), "actual process starts hidden and is tracked");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            Check(!Smart_Training_Server.Helper.ProcessHelper.IsProcessRunning(fixtureId), "actual child exit is detected");
        }
        finally { Smart_Training_Server.Helper.ProcessHelper.KillProcessById(fixtureId); }
        Check(Smart_Training_Server.Helper.ProcessHelper.GetProcessById(fixtureId) == null, "actual exited process handle is released");
    }

    private sealed class TestProcesses : IRoomServerProcessManager
    {
        private readonly HashSet<string> running = new();
        public bool FailLaunch { get; set; }
        public string LastPath { get; private set; } = "";
        public int Launches { get; private set; }
        public void Launch(string roomId, int port, string executablePath, string serverToken)
        {
            LastPath = executablePath;
            if (FailLaunch) throw new IOException("simulated launch failure");
            Launches++; running.Add(roomId);
        }
        public bool IsRunning(string id) => running.Contains(id);
        public void Stop(string id) => running.Remove(id);
        public void Crash(string id) => running.Remove(id);
    }
}

