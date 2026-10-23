using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Operations;
using Smart_Training_Server.Services.Room;

internal static class ShutdownChecks
{
    private sealed class RoomProcesses : IRoomServerProcessManager
    {
        public readonly HashSet<string> Running = new();

        public int Launch(string id, string exe, string secret) { Running.Add(id); return 6000; }

        public void Stop(string id) => Running.Remove(id);

        public bool IsRunning(string id) => Running.Contains(id);
    }

    public static async Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: shutdown " + name);
        }

        var folder = Path.Combine(Path.GetTempPath(), "shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "history.json");
            var registry = new ServerOperationsRegistry(file);
            var users = new UserService(Options.Create(new AuthOptions {
                        NameMinLength = 1,
                        NameMaxLength = 30
                    }));
            var rp = new RoomProcesses();
            var lobbies = new LobbyService(new ContentService(), users, registry);
            lobbies.InitializeCatalogLobbies();
            var rooms = new RoomService(
                lobbies,
                users,
                Options.Create(new RoomOptions {
                        NameMinLength = 1,
                        NameMaxLength = 30,
                        MinUsers = 1,
                        MaxUsers = 4
                    }),
                processManager: rp,
                operations: registry
            );
            var service = new LobbyOperatingModeService(users, lobbies, rooms, registry);
            var a = users.Login(new LoginRequest { UserName = "shutdown-a" }).User!;
            var b = users.Login(new LoginRequest { UserName = "shutdown-b" }).User!;
            var c = users.Login(new LoginRequest { UserName = "shutdown-c" }).User!;
            Task<Smart_Training_Server.Responses.Lobby.JoinLobbyResponse> Join(string uid, string id) => lobbies.JoinLobby(uid, new LobbyMembershipRequest { LobbyId = id });
            Check(registry.GetShutdown("sample-training").State == "none", "default is no schedule");
            Check(
                service.SetOperatingMode("missing", "scheduled", DateTimeOffset.UtcNow.AddMinutes(1), "x", 0).StatusCode == 404,
                "unknown lobby rejected"
            );
            Check(
                service.SetOperatingMode("sample-training", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-1), "x", 0).StatusCode == 400,
                "past time rejected"
            );
            Check(
                service.SetOperatingMode("sample-training", "scheduled", DateTimeOffset.UtcNow.AddDays(31), "x", 0).StatusCode == 400,
                "over 30 days rejected"
            );
            Check(
                service.SetOperatingMode("sample-training", "scheduled", DateTimeOffset.UtcNow.AddMinutes(1), " ", 0).StatusCode == 400,
                "blank reason rejected"
            );
            var entry = Join(a.Id, "sample-training");
            Check((await entry).Success, "initial lobby entry");
            var other = Join(c.Id, "safety-training");
            await other;
            var room = TestRoomSetup.Create(rooms,
                a.Id,
                new CreateRoomRequest {
                    LobbyId = "sample-training",
                    ContentId = "sample-training",
                    RoomName = "scheduled-room",
                    MaxUsers = 2
                }
            ).Room!;
            var task = rooms.JoinRoom(a.Id, new JoinRoomRequest {
                    LobbyId = "sample-training",
                    RoomId = room.Id
                });
            rooms.RegisterServer(room.Id, room.ServerToken, "room-a");
            var res = await task;
            rooms.ConfirmConnection(room.Id, "room-a", a.Id, res.AdmissionToken);
            var due = DateTimeOffset.UtcNow.AddMinutes(5);
            var set = service.SetOperatingMode("sample-training", "scheduled", due, "업데이트", 0);
            Check(set.Success && set.PersistenceWarning == null && set.Operation?.Revision == 1, "schedule saved immediately");
            Check(
                lobbies.GetLobbySummary("sample-training")is { ShutdownState: "scheduled", ShutdownReason: "업데이트" },
                "public notice published"
            );
            Check(
                !(await Join(b.Id, "sample-training")).Success,
                "new entry rejected without reservation"
            );
            service.ExecuteDue(due.UtcDateTime.AddSeconds(-1));
            Check(a.CurrentRoomId == room.Id && rp.Running.Contains(room.Id), "existing connection survives before deadline");
            Check(service.SetOperatingMode("sample-training", "running", null, "취소", 0).StatusCode == 409, "stale cancellation rejected");
            Check(service.SetOperatingMode("sample-training", "running", null, "취소", 1).Success, "operator can cancel before deadline");
            service.ExecuteDue(due.UtcDateTime.AddSeconds(1));
            Check(a.CurrentRoomId == room.Id, "cancelled reservation never shuts down");
            var accepted = Join(b.Id, "sample-training");
            Check((await accepted).Success, "cancellation reopens admission");
            var pendingRoom = rooms.JoinRoom(b.Id, new JoinRoomRequest {
                    LobbyId = "sample-training",
                    RoomId = room.Id
                });
            var pendingRes = await pendingRoom;
            Check(b.PendingRoomId == room.Id, "pending room reservation established");
            service.SetOperatingMode("sample-training", "scheduled", due, "재예약", 2);
            service.ExecuteDue(due.UtcDateTime);
            Check(registry.GetShutdown("sample-training").State == "completed", "deadline completes schedule");
            Check(
                a.CurrentLobbyId == "" && a.CurrentRoomId == "" && b.CurrentLobbyId == "" && b.PendingRoomId == "",
                "all memberships and reservations cleared"
            );
            Check(
                rooms.FindById(room.Id) == null && !rp.Running.Contains(room.Id),
                "target rooms stop while lobby membership is evacuated"
            );
            Check(
                c.CurrentLobbyId == "safety-training",
                "other lobby and member preserved"
            );
            Check(
                !rooms.ConfirmConnection(room.Id, "room-a", b.Id, pendingRes.AdmissionToken).Success,
                "late room acknowledgement rejected"
            );
            Check(users.FindById(a.Id) == a, "login survives evacuation");
            Check(!(await Join(a.Id, "sample-training")).Success, "completed lobby stays closed");
            Check(lobbies.GetLobbySummary("sample-training")!.UserCount == 0, "evacuated lobby remains empty");
            int events = registry.Snapshot().Events.Count;
            service.ExecuteDue(due.UtcDateTime.AddSeconds(5));
            Check(registry.Snapshot().Events.Count == events, "deadline tick is idempotent");
            Check(
                !registry.Snapshot().Events.Any(x => x.Action == "failed" && x.Reason == "로비 종료 예약 실행"),
                "planned closure is not a failure"
            );
            var restored = new ServerOperationsRegistry(file);
            Check(
                restored.GetShutdown("sample-training")is { State: "completed", Reason: "재예약", Revision: 4 },
                "completed block persists after restart"
            );
            Check(
                service.SetOperatingMode("sample-training", "running", null, "재개", 4).Success && !registry.GetMaintenance("sample-training").Enabled,
                "one resume clears completed maintenance and shutdown"
            );
            var reopened = Join(a.Id, "sample-training");
            Check((await reopened).Success, "one resume permits immediate lobby entry");
            var future = service.SetOperatingMode("safety-training", "scheduled", due, "미래 예약", 0);
            var loaded = new ServerOperationsRegistry(file);
            Check(
                loaded.GetShutdown("safety-training").AtUtc == due.UtcDateTime,
                "future schedule persists with exact UTC time"
            );
            loaded.CompleteSession();
            Check(
                loaded.SetOperatingMode("safety-training", "safety", "running", "x", null, 1).StatusCode == 503,
                "closing registry rejects changes"
            );
            var d = users.Login(new LoginRequest { UserName = "shutdown-pending" }).User!;
            var pendingLobby = Join(d.Id, "emergency-training");
            var pendingDue = DateTimeOffset.UtcNow.AddMinutes(1);
            service.SetOperatingMode("emergency-training", "scheduled", pendingDue, "준비 중 종료", 0);
            Check(d.CurrentLobbyId == "emergency-training", "member stays in lobby before scheduled deadline");
            service.ExecuteDue(pendingDue.UtcDateTime);
            Check(
                (await pendingLobby).Success && d.CurrentLobbyId == "",
                "deadline clears membership admitted before closure"
            );
            Check(lobbies.GetLobbySummary("emergency-training")!.UserCount == 0, "closure leaves no lobby members");
            Check(!(await Join(d.Id, "emergency-training")).Success, "completed closure rejects fresh entry");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
