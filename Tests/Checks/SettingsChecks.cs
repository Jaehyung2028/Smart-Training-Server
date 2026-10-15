using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class SettingsChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { NameMinLength = 2, NameMaxLength = 30, MinUsers = 1, MaxUsers = 8, AllowDuplicateName = false }));
        var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "settings" }).Lobby!;
        lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
        async Task<UserInfo> Add(string name)
        {
            var user = users.Login(new LoginRequest { UserName = name }).User!;
            await lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = lobby.Id }); return user;
        }
        var owner = await Add("owner"); var member = await Add("member"); var pending = await Add("pending");
        var room = rooms.CreateRoom(new CreateRoomRequest { UserId = owner.Id, LobbyId = lobby.Id, RoomName = "initial", ContentId = "test", MaxUsers = 3 }).Room!;
        room.State = Smart_Training_Server.Enum.ServerState.Starting; room.ServerToken = "fixture";
        rooms.RegisterServer(room.Id, "fixture", "settings-server");
        var tokens = new Dictionary<string, string>();
        foreach (var user in new[] { owner, member })
        {
            var admission = await rooms.JoinRoom(new JoinRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomId = room.Id });
            tokens[user.Id] = admission.AdmissionToken;
            Check(rooms.ConfirmConnection(room.Id, "settings-server", user.Id, admission.AdmissionToken).Success, "settings fixture confirms participant");
        }
        UpdateRoomSettingsRequest Request(string name, int? count) => new() { LobbyId = lobby.Id, RoomId = room.Id, RoomName = name, MaxUsers = count };
        Check(!rooms.UpdateRoomSettings(member.Id, Request("changed", 3)).Success, "settings reject non-master");
        foreach (var request in new[] { Request("", 3), Request("x", 3), Request(new string('x', 31), 3), Request("valid", null), Request("valid", 0), Request("valid", 9), Request("valid", 1) })
            Check(!rooms.UpdateRoomSettings(owner.Id, request).Success && room.Name == "initial" && room.MaxUsers == 3, "invalid settings preserve both fields");
        var wrong = Request("changed", 3); wrong.LobbyId = "wrong";
        Check(!rooms.UpdateRoomSettings(owner.Id, wrong).Success, "settings reject mismatched lobby");
        var reserved = await rooms.JoinRoom(new JoinRoomRequest { UserId = pending.Id, LobbyId = lobby.Id, RoomId = room.Id });
        Check(!rooms.UpdateRoomSettings(owner.Id, Request("changed", 2)).Success && rooms.ValidateConnection(room.Id, "settings-server", pending.Id, reserved.AdmissionToken).Success,
            "settings preserve reserved slot and admission token");
        rooms.LeaveRoom(new LeaveRoomRequest { UserId = pending.Id, LobbyId = lobby.Id, RoomId = room.Id });
        var duplicate = rooms.CreateRoom(new CreateRoomRequest { UserId = pending.Id, LobbyId = lobby.Id, RoomName = "duplicate", ContentId = "test", MaxUsers = 3 }).Room!;
        Check(!rooms.UpdateRoomSettings(owner.Id, Request("DUPLICATE", 2)).Success, "settings enforce case-insensitive duplicate-name policy");
        member.RoomIndex = 7;
        var result = rooms.UpdateRoomSettings(owner.Id, Request("  renamed  ", 2));
        Check(result.Success && room.Name == "renamed" && room.MaxUsers == 2 && owner.CurrentRoomName == "renamed" && member.CurrentRoomName == "renamed" && member.RoomIndex == 7,
            "settings trim name, update all participant names and preserve existing indexes");
        Check(rooms.UpdateRoomSettings(owner.Id, Request("renamed", 3)).Success, "same room name is allowed when duplicate names disabled");
        reserved = await rooms.JoinRoom(new JoinRoomRequest { UserId = pending.Id, LobbyId = lobby.Id, RoomId = room.Id });
        Check(rooms.ConfirmConnection(room.Id, "settings-server", pending.Id, reserved.AdmissionToken).Success && pending.RoomIndex != member.RoomIndex,
            "capacity expansion admits participant with a distinct index after shrink");
        var snapshot = rooms.GetRoomMaster(room.Id, "settings-server");
        Check(snapshot.RoomName == "renamed" && snapshot.MaxUsers == 3, "registered server receives current room settings");
        rooms.TransferRoomMaster(owner.Id, new TransferRoomMasterRequest { LobbyId = lobby.Id, RoomId = room.Id, TargetUserId = member.Id });
        Check(!rooms.UpdateRoomSettings(owner.Id, Request("old-owner", 3)).Success && rooms.UpdateRoomSettings(member.Id, Request("new-owner", 3)).Success,
            "settings permission transfers immediately with room master");
        string roster = System.Text.Json.JsonSerializer.Serialize(room.Users.Keys.ToArray());
        Check(!rooms.SetRoomPhase(room.Id, "unregistered", member.Id, tokens[member.Id], true, roster, room.PhaseRevision).Success,
            "phase requires registered server");
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", member.Id, "wrong-token", true, roster, room.PhaseRevision).Success,
            "phase requires current master admission token");
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", owner.Id, tokens[owner.Id], true, roster, room.PhaseRevision).Success,
            "former master cannot change phase");
        foreach (var invalid in new[] { "[]", "null", "{}", "not-json", "[\"missing-user\"]" })
            Check(!rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, invalid, room.PhaseRevision).Success && !room.IsRunning,
                "phase rejects malformed or mismatched roster without mutation");
        var phase = rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, roster, room.PhaseRevision);
        Check(phase.Success && room.IsRunning && phase.PhaseRevision == 1 && rooms.GetRoomByLobby(lobby.Id, room.Id)!.IsRunning,
            "phase starts atomically and is included in query snapshot");
        Check(rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, roster, room.PhaseRevision).PhaseRevision == 1,
            "duplicate phase request does not advance revision");
        Check(!rooms.UpdateRoomSettings(member.Id, Request("blocked", 3)).Success, "running phase blocks settings");
        var outside = await Add("outside");
        Check(!(await rooms.JoinRoom(new JoinRoomRequest { UserId = outside.Id, LobbyId = lobby.Id, RoomId = room.Id })).Success,
            "running phase blocks new reservations");
        Check(rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], false, "[]", room.PhaseRevision).Success &&
            !room.IsRunning && room.PhaseRevision == 2, "phase returns to waiting with monotonic revision");
        rooms.UpdateRoomSettings(member.Id, Request("new-owner", 4));
        var outstanding = await rooms.JoinRoom(new JoinRoomRequest { UserId = outside.Id, LobbyId = lobby.Id, RoomId = room.Id });
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, roster, room.PhaseRevision).Success &&
            rooms.ValidateConnection(room.Id, "settings-server", outside.Id, outstanding.AdmissionToken).Success,
            "phase start preserves unfinished reservation and rejects transition");
        rooms.LeaveRoom(new LeaveRoomRequest { UserId = outside.Id, LobbyId = lobby.Id, RoomId = room.Id });
        rooms.SetRoomEntryLock(member.Id, new SetRoomEntryLockRequest { LobbyId = lobby.Id, RoomId = room.Id, IsEntryLocked = true });
        rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, roster, room.PhaseRevision);
        Check(rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], false, "[]", room.PhaseRevision).Success && room.IsEntryLocked,
            "return to waiting preserves separate manual entry lock");
        rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], true, roster, room.PhaseRevision);
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], false, "[]", 0).Success && room.IsRunning,
            "delayed phase command cannot overwrite newer running state");
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", null!, tokens[member.Id], false, "[]", room.PhaseRevision).Success,
            "missing phase caller is rejected without exception");
        rooms.ClientDisconnected(room.Id, "settings-server", member.Id, tokens[member.Id]);
        Check(room.IsRunning && room.MasterUserId == owner.Id && room.Users.Count == 2,
            "running master disconnect transfers authority and preserves phase");
        Check(!rooms.SetRoomPhase(room.Id, "settings-server", member.Id, tokens[member.Id], false, "[]", room.PhaseRevision).Success && room.IsRunning,
            "disconnected master cannot complete a delayed transition");
        Check(rooms.SetRoomPhase(room.Id, "settings-server", owner.Id, tokens[owner.Id], false, "[]", room.PhaseRevision).Success,
            "new master can return to waiting after disconnect");
        rooms.SetRoomEntryLock(owner.Id, new SetRoomEntryLockRequest { LobbyId = lobby.Id, RoomId = room.Id, IsEntryLocked = false });
        var fresh = await rooms.JoinRoom(new JoinRoomRequest { UserId = member.Id, LobbyId = lobby.Id, RoomId = room.Id });
        Check(fresh.Success && rooms.ConfirmConnection(room.Id, "settings-server", member.Id, fresh.AdmissionToken).Success,
            "disconnected member rejoins with a fresh admission");
        Check(!rooms.ClientDisconnected(room.Id, "settings-server", member.Id, tokens[member.Id]).Success && member.CurrentRoomId == room.Id,
            "old disconnect callback cannot remove reconnected member");
        rooms.SetRoomPhase(room.Id, "settings-server", owner.Id, tokens[owner.Id], true,
            System.Text.Json.JsonSerializer.Serialize(room.Users.Keys.ToArray()), room.PhaseRevision);
        rooms.ServerDisconnected(room.Id, "settings-server");
        Check(!room.IsRunning && room.Users.IsEmpty, "dedicated server reset clears phase and connected membership");
    }
}

