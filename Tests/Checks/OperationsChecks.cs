using Smart_Training_Server.Services.Operations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class OperationsChecks
{
    private sealed class RoomProcesses : IRoomServerProcessManager
    {
        public bool Running;

        public int Launch(string id, string executable, string secret) { Running = true; return 6000; }

        public bool IsRunning(string id) => Running;

        public void Stop(string id) => Running = false;
    }

    public static async Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: operations " + name);
        }

        var registry = new ServerOperationsRegistry();
        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var roomProcess = new RoomProcesses();
        var lobbies = new LobbyService(
            new ContentService(),
            users,
            registry
        );
        lobbies.InitializeCatalogLobbies();
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 4
                }),
            processManager: roomProcess,
            operations: registry
        );
        var operations = new ServerOperationsService(users, lobbies, rooms, registry);
        var initial = operations.GetSnapshot();
        Check(
            initial.Lobbies.Count == 3 && initial.Rooms.Count == 0 && initial.RecentEvents.Count == 0,
            "initial snapshot lists catalog without launching any process"
        );
        var login = users.Login(new LoginRequest { UserName = "ops-user" });
        var user = login.User!;
        var join = lobbies.JoinLobby(user.Id, new LobbyMembershipRequest { LobbyId = "sample-training" });
        var pending = operations.GetSnapshot().Lobbies.Single(x => x.Id == "sample-training");
        Check(
            pending.State == "running" && pending.PendingUsers == 0 && pending.ConnectedUsers == 1 && pending.Lifecycle == null && pending.ProcessId == null && !pending.ProcessRunning,
            "lobby entry immediately reports users without a process or readiness wait"
        );
        await join;
        Check(
            operations.GetSnapshot().Lobbies.Single(x => x.Id == "sample-training").ConnectedUsers == 1,
            "lobby membership is available without worker registration"
        );
        var room = TestRoomSetup.Create(rooms,
            user.Id,
            new CreateRoomRequest {
                LobbyId = "sample-training",
                ContentId = "sample-training",
                RoomName = "ops-room",
                MaxUsers = 2
            }
        ).Room!;
        var roomJoin = rooms.JoinRoom(user.Id, new JoinRoomRequest {
                LobbyId = "sample-training",
                RoomId = room.Id
            });
        var roomStarting = operations.GetSnapshot().Rooms.Single();
        Check(
            roomStarting.PendingUsers == 1 && roomStarting.MaxUsers == 2 && roomStarting.State == "starting" && roomStarting.Lifecycle?.StartedAtUtc != null,
            "starting room reports admission and capacity"
        );
        string secret = room.ServerToken;
        rooms.RegisterServer(room.Id, secret, "ops-room-connection");
        var admission = await roomJoin;
        rooms.ConfirmConnection(room.Id, "ops-room-connection", user.Id, admission.AdmissionToken);
        var connected = operations.GetSnapshot();
        Check(
            connected.Rooms.Single().ConnectedUsers == 1 && connected.Rooms.Single().PendingUsers == 0 && connected.Rooms.Single().Lifecycle?.State == "running",
            "confirmed connection replaces reservation in operations snapshot"
        );
        var stableEvents = registry.Snapshot().Events.Count;
        var first = operations.GetSnapshot();
        var second = operations.GetSnapshot();
        Check(
            registry.Snapshot().Events.Count == stableEvents && room.Users.Count == 1 && user.CurrentRoomId == room.Id,
            "repeated status reads do not change lifecycle or membership"
        );
        string json = JsonSerializer.Serialize(connected);
        Check(
            !json.Contains(secret) && !json.Contains(login.AccessToken) && !json.Contains(admission.AdmissionToken) && !json.Contains(user.Id),
            "operations DTO excludes authentication and participant secrets"
        );
        rooms.ServerDisconnected(room.Id, "ops-room-connection");
        string failedRoomId = room.Id;
        var failed = operations.GetSnapshot().RecentProcesses.Single(x => x.Id == failedRoomId);
        Check(
            operations.GetSnapshot().Rooms.Count == 0 && failed.FailedAtUtc != null && failed.StoppedAtUtc != null && failed.LastFailure == "SignalR 연결 종료",
            "unexpected room shutdown records time and failure reason"
        );
        Check(
            connected.Rooms.Single().ConnectedUsers == 1 && connected.Rooms.Single().Lifecycle?.LastFailure == null,
            "previous snapshots remain independent of later mutations"
        );
        room = TestRoomSetup.Create(rooms, user.Id, new CreateRoomRequest { LobbyId = "sample-training", RoomName = "ops-replacement", ContentId = "sample-training", MaxUsers = 2 }).Room!;
        var retry = rooms.JoinRoom(user.Id, new JoinRoomRequest {
                LobbyId = "sample-training",
                RoomId = room.Id
            });
        rooms.RegisterServer(room.Id, room.ServerToken, "ops-retry");
        var retried = await retry;
        rooms.ConfirmConnection(room.Id, "ops-retry", user.Id, retried.AdmissionToken);
        Check(
            operations.GetSnapshot().Rooms.Single().Lifecycle is { State: "running", StartCount: 1, LastFailure: null } && operations.GetSnapshot().RecentProcesses.Any(x => x.Id == failedRoomId && x.LastFailure == "SignalR 연결 종료"),
            "replacement room starts independently and preserves deleted-room failure history"
        );
        var lifecycle = new UserLifecycleService(users, rooms, lobbies, new RecordingRoomHub(), NullLogger<UserLifecycleService>.Instance);
        await lifecycle.LogoutAsync(user.Id);
        var ended = operations.GetSnapshot();
        Check(
            ended.Rooms.Count == 0 && ended.RecentProcesses.Any(x => x.Id == failedRoomId && x.StoppedAtUtc != null && x.LastFailure == "SignalR 연결 종료"),
            "deleted room retains bounded historical failure and stop record"
        );
        Check(
            ended.Lobbies.Single(x => x.Id == "sample-training").ConnectedUsers == 0 && ended.RecentEvents.All(x => x.Kind != "lobby" || x.Action != "starting"),
            "last logout clears lobby users without a worker lifecycle"
        );
        var timeoutUser = users.Login(new LoginRequest { UserName = "ops-timeout" }).User!;
        await lobbies.JoinLobby(timeoutUser.Id, new LobbyMembershipRequest { LobbyId = "sample-training" });
        var timedOut = operations.GetSnapshot().Lobbies.Single(x => x.Id == "sample-training");
        Check(
            timedOut.PendingUsers == 0 && timedOut.ConnectedUsers == 1 && timedOut.Lifecycle == null && !timedOut.ProcessRunning,
            "fresh lobby entry succeeds immediately without worker failure or timeout"
        );
        OperationsController Controller(string? address, string host, string? origin = null)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = address == null ? null : IPAddress.Parse(address);
            context.Request.Host = new HostString(host);
            context.Request.Scheme = "http";
            if (origin != null)
            {
                context.Request.Headers.Origin = origin;
            }

            return new OperationsController(operations)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = context
                }
            };
        }

        foreach (var address in new[]
        {
            "127.0.0.1",
            "::1",
            "::ffff:127.0.0.1"
        }

        )
        {
            Check(
                Controller(address, "localhost:5229").GetStatus() is OkObjectResult,
                "loopback status access allowed for " + address
            );
        }

        Check(
            Controller("203.0.113.7", "localhost:5229").GetStatus()is ObjectResult { StatusCode: 403 },
            "remote client cannot access operations with localhost Host"
        );
        var forwarded = Controller("203.0.113.7", "localhost:5229");
        forwarded.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        forwarded.Request.Headers.Authorization = "Bearer arbitrary-user-token";
        Check(
            forwarded.GetStatus()is ObjectResult { StatusCode: 403 },
            "user token and spoofed forwarding headers do not grant remote access"
        );
        Check(Controller(null, "localhost:5229").GetStatus()is ObjectResult { StatusCode: 403 }, "unknown peer rejected");
        Check(
            Controller("127.0.0.1", "attacker.example:5229").Page()is ObjectResult { StatusCode: 403 },
            "nonlocal Host rejected for dashboard"
        );
        Check(
            Controller("127.0.0.1", "localhost:5229", "https://attacker.example").GetStatus()is ObjectResult { StatusCode: 403 },
            "cross origin browser access rejected"
        );
        Check(
            Controller("127.0.0.1", "localhost:5229", "http://localhost:5229").GetStatus() is OkObjectResult,
            "same origin browser access allowed"
        );
        var page = Controller("127.0.0.1", "127.0.0.1:5229");
        Check(
            page.Page()is ContentResult { Content: not null } content && content.Content.Contains("textContent") && !content.Content.Contains("innerHTML") && content.Content.Contains("setInterval(refresh,5000)"),
            "dashboard safely renders text and refreshes automatically"
        );
        string dashboard = ((ContentResult)page.Page()).Content!;
        string lobbyTable = dashboard.Split("<h2>콘텐츠 로비</h2>")[1].Split("</table>")[0];
        Check(!lobbyTable.Contains("PID") && !lobbyTable.Contains("입장 대기") && lobbyTable.Contains("사용자") && lobbyTable.Contains("운영 관리"),
            "lobby dashboard shows membership and operating mode without worker-only columns");
        Check(page.Response.Headers.CacheControl == "no-store", "dashboard responses are not cached");
        var bounded = new ServerOperationsRegistry();
        bounded.Record("lobby", "active", "active", "active", "starting");
        for (int i = 0; i < 514; i++)
        {
            bounded.Record("room", i.ToString(), "fixture", "fixture", "starting");
            bounded.Record("room", i.ToString(), "fixture", "fixture", "stopped");
        }

        var history = bounded.Snapshot();
        Check(
            history.Events.Count == 200 && history.Processes.Count == 513 && history.Processes.Any(x => x.Id == "active" && x.State == "starting"),
            "event history and inactive records are bounded without evicting active processes"
        );
        int before = history.Events.Count;
        bounded.Record("room", "513", "fixture", "fixture", "stopped");
        Check(
            bounded.Snapshot().Events.Count == before && bounded.Snapshot().Events[0] == history.Events[0],
            "repeated cleanup does not append duplicate stop events"
        );
    }
}
