using Smart_Training_Server.Services.Operations;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Operations;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class MaintenanceChecks
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

            Console.WriteLine("PASS: maintenance " + name);
        }

        var folder = Path.Combine(Path.GetTempPath(), "maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "history.json");
            var registry = new ServerOperationsRegistry(file);
            var users = new UserService(Options.Create(new AuthOptions {
                        NameMinLength = 1,
                        NameMaxLength = 20
                    }));
            var lobbies = new LobbyService(new ContentService(), users, registry);
            lobbies.InitializeCatalogLobbies();
            var roomProcesses = new RoomProcesses();
            var rooms = new RoomService(
                lobbies,
                users,
                Options.Create(new RoomOptions {
                        NameMinLength = 1,
                        NameMaxLength = 20,
                        MinUsers = 1,
                        MaxUsers = 4
                    }),
                processManager: roomProcesses,
                operations: registry
            );
            var modeService = new LobbyOperatingModeService(users, lobbies, rooms, registry);
            var ops = new ServerOperationsService(users, lobbies, rooms, registry);
            var a = users.Login(new LoginRequest { UserName = "maintenance-a" }).User!;
            var b = users.Login(new LoginRequest { UserName = "maintenance-b" }).User!;
            Task<Smart_Training_Server.Responses.Lobby.JoinLobbyResponse> Join(string id) => lobbies.JoinLobby(id, new LobbyMembershipRequest { LobbyId = "sample-training" });
            Check(
                lobbies.GetLobbySummary("sample-training")is { IsMaintenance: false, MaintenanceRevision: 0 },
                "catalog starts in normal mode"
            );
            var on = modeService.SetOperatingMode("sample-training", "maintenance", null, "정기 점검", 0);
            Check(
                on.Success && on.PersistenceWarning == null && on.Operation is { Mode: "maintenance", Revision: 1, ChangedAtUtc: not null } && File.Exists(file),
                "enable is saved with timestamp and revision"
            );
            Check(
                registry.Snapshot().Processes.Count == 0,
                "maintenance does not launch or alter a process lifecycle"
            );
            var denied = await Join(a.Id);
            Check(
                !denied.Success && denied.Message.Contains("정기 점검"),
                "new admission is rejected before reservation or process launch"
            );
            Check(
                lobbies.GetLobbySummary("sample-training")is { IsMaintenance: true, MaintenanceReason: "정기 점검" },
                "public summary contains maintenance notice"
            );
            int eventCount = registry.Snapshot().Events.Count;
            Check(
                modeService.SetOperatingMode("sample-training", "maintenance", null, "정기 점검", 0).Success && registry.Snapshot().Events.Count == eventCount,
                "duplicate request is idempotent without extra event"
            );
            Check(
                modeService.SetOperatingMode("sample-training", "running", null, "해제", 0).StatusCode == 409 && registry.GetMaintenance("sample-training").Enabled,
                "stale revision cannot overwrite current maintenance"
            );
            Check(
                modeService.SetOperatingMode("sample-training", "maintenance", null, " ", 1).StatusCode == 400 && modeService.SetOperatingMode("sample-training", "maintenance", null, new string ('x', 201), 1).StatusCode == 400,
                "blank and oversized reasons are rejected"
            );
            Check(
                modeService.SetOperatingMode("missing", "maintenance", null, "점검", 0).StatusCode == 404,
                "unknown catalog lobby cannot be changed"
            );
            Check(modeService.SetOperatingMode("sample-training", "running", null, "점검 완료", 1).Success, "maintenance can be released");
            var pending = Join(a.Id);
            Check(
                a.CurrentLobbyId == "sample-training",
                "admission commits before maintenance switch"
            );
            Check(
                modeService.SetOperatingMode("sample-training", "maintenance", null, "추가 점검", 2).Success && a.CurrentLobbyId == "sample-training",
                "maintenance preserves existing lobby membership"
            );
            Check(
                (await pending).Success && a.CurrentLobbyId == "sample-training",
                "member admitted before maintenance remains in the lobby"
            );
            Check(
                !(await Join(b.Id)).Success,
                "maintenance blocks subsequent lobby entries"
            );
            var room = TestRoomSetup.Create(rooms,
                a.Id,
                new CreateRoomRequest {
                    LobbyId = "sample-training",
                    ContentId = "sample-training",
                    RoomName = "maintenance-room",
                    MaxUsers = 2
                }
            ).Room!;
            var admission = rooms.JoinRoom(a.Id, new JoinRoomRequest {
                    LobbyId = "sample-training",
                    RoomId = room.Id
                });
            rooms.RegisterServer(room.Id, room.ServerToken, "maintenance-room-server");
            var reservation = await admission;
            Check(
                rooms.ConfirmConnection(room.Id, "maintenance-room-server", a.Id, reservation.AdmissionToken).Success && room.Users.Count == 1,
                "existing lobby participant can create and join a room during maintenance"
            );
            var live = ops.GetSnapshot();
            Check(
                live.Lobbies.Single(x => x.Id == "sample-training").Maintenance?.Enabled == true && live.Rooms.Single().ConnectedUsers == 1 && live.Rooms.Single().Lifecycle?.State == "running",
                "maintenance does not overwrite active room readiness"
            );
            Check(
                modeService.SetOperatingMode("sample-training", "maintenance", null, "사유 변경", 3).Success && room.Users.Count == 1 && roomProcesses.Running,
                "reason update preserves existing multiplayer connection"
            );
            Check(
                lobbies.GetLobbySummary("safety-training")is { IsMaintenance: false, MaintenanceRevision: 0 },
                "another lobby is unaffected"
            );
            OperationsController Controller(string peer, string? origin, bool custom)
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                context.Request.Scheme = "http";
                context.Request.Host = new HostString("localhost:5229");
                context.Request.ContentType = "application/json";
                if (origin != null)
                {
                    context.Request.Headers.Origin = origin;
                }

                if (custom)
                {
                    context.Request.Headers["X-Operations-Request"] = "1";
                }

                return new OperationsController(ops, modeService)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = context
                    }
                };
            }

            var request = new SetOperatingModeRequest
            {
                Mode = "maintenance",
                Reason = "운영 점검",
                ExpectedRevision = 0
            };
            Check(
                Controller("203.0.113.7", null, true).SetOperatingMode("safety-training", request)is ObjectResult { StatusCode: 403 },
                "remote mutation is blocked even with custom header"
            );
            Check(
                Controller("127.0.0.1", null, false).SetOperatingMode("safety-training", request)is ObjectResult { StatusCode: 403 },
                "missing mutation header is rejected"
            );
            Check(
                Controller("127.0.0.1", "https://attacker.example", true).SetOperatingMode("safety-training", request)is ObjectResult { StatusCode: 403 },
                "cross-origin mutation is rejected"
            );
            Check(
                Controller("127.0.0.1", "http://localhost:5229", true).SetOperatingMode("safety-training", request) is OkObjectResult,
                "local same-origin mutation with custom header is accepted"
            );
            rooms.LeaveRoom(a.Id, new RoomMembershipRequest {
                    LobbyId = "sample-training",
                    RoomId = room.Id
                });
            lobbies.LeaveLobby(a.Id, new LobbyMembershipRequest { LobbyId = "sample-training" });
            registry.FlushHistory();
            Check(
                registry.GetMaintenance("sample-training").Enabled,
                "last departure preserves maintenance mode"
            );
            var restoredRegistry = new ServerOperationsRegistry(file);
            var restored = new LobbyService(new ContentService(), users, restoredRegistry);
            restored.InitializeCatalogLobbies();
            var restoredModeService = new LobbyOperatingModeService(users, restored, rooms, restoredRegistry);
            Check(
                restored.GetLobbySummary("sample-training")is { IsMaintenance: true, MaintenanceReason: "사유 변경", MaintenanceRevision: 4 },
                "restart restores maintenance without launching prior processes"
            );
            Check(
                !(await restored.JoinLobby(b.Id, new LobbyMembershipRequest { LobbyId = "sample-training" })).Success,
                "restored maintenance blocks new admission"
            );
            Check(
                restoredModeService.SetOperatingMode("sample-training", "running", null, "운영 재개", 4).Success,
                "restored maintenance can be released using current revision"
            );
            var next = restored.JoinLobby(b.Id, new LobbyMembershipRequest { LobbyId = "sample-training" });
            Check((await next).Success, "new admission succeeds after maintenance release");
            Check(
                restoredRegistry.Snapshot().Events.Any(x => x.Action == "maintenance_on") && restoredRegistry.Snapshot().Events.Any(x => x.Action == "maintenance_off"),
                "enable and release events retain reason and time across restart"
            );
            restoredRegistry.CompleteSession();
            Check(
                restoredModeService.SetOperatingMode("sample-training", "maintenance", null, "종료 중", 5).StatusCode == 503,
                "shutdown prevents further maintenance changes"
            );
            var old = Path.Combine(folder, "old.json");
            File.WriteAllText(old, "{\"Version\":1,\"Processes\":[],\"Events\":[]}");
            Check(
                !new ServerOperationsRegistry(old).GetMaintenance("sample-training").Enabled,
                "previous history format without maintenance remains compatible"
            );
        }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Unsafe fixture path");
            }

            Directory.Delete(folder, true);
        }
    }
}
