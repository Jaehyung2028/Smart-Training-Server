using System.Text.Json;
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

internal static class RoomChatChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var clock = new TestClock();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }), clock);
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 8 }));
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "chat" }).Lobby!;
        lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
        var ids = new[] { "a", "b", "pending", "other" };
        var members = new Dictionary<string, UserInfo>();
        var tokens = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            var login = users.Login(new LoginRequest { UserName = id }); members[id] = login.User!;
            await lobbies.JoinLobby(new JoinLobbyRequest { UserId = login.User!.Id, LobbyId = lobby.Id });
            updates.Subscribe(id, lobby.Id, "Bearer " + login.AccessToken); tokens[id] = login.AccessToken!;
        }
        RoomInfo Create(string name)
        {
            var room = rooms.CreateRoom(new CreateRoomRequest { UserId = members["a"].Id, LobbyId = lobby.Id, RoomName = name, ContentId = "test", MaxUsers = 4 }).Room!;
            room.State = Smart_Training_Server.Enum.ServerState.Starting; room.ServerToken = "fixture";
            rooms.RegisterServer(room.Id, "fixture", name); return room;
        }
        var room = Create("main"); var otherRoom = Create("other-room");
        var admissions = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            var target = id == "other" ? otherRoom : room;
            var admission = await rooms.JoinRoom(new JoinRoomRequest { UserId = members[id].Id, LobbyId = lobby.Id, RoomId = target.Id });
            admissions[id] = admission.AdmissionToken;
            if (id != "pending") rooms.ConfirmConnection(target.Id, target == room ? "main" : "other-room", members[id].Id, admission.AdmissionToken);
        }
        var aBind = Guid.NewGuid().ToString("N"); var bBind = Guid.NewGuid().ToString("N"); var otherBind = Guid.NewGuid().ToString("N");
        Check(!updates.SubscribeRoomChat("unknown", room.Id, admissions["a"], aBind), "room chat requires authenticated lobby subscription");
        Check(!updates.SubscribeRoomChat("pending", room.Id, admissions["pending"], aBind), "reservation alone cannot subscribe to room chat");
        Check(!updates.SubscribeRoomChat("other", room.Id, admissions["other"], otherBind), "other room participant cannot subscribe");
        Check(!updates.SubscribeRoomChat("a", room.Id, "stale", aBind) && !updates.SubscribeRoomChat("a", room.Id, admissions["a"], "bad"), "room chat rejects stale admission and invalid binding");
        Check(updates.SubscribeRoomChat("a", room.Id, admissions["a"], aBind) && updates.SubscribeRoomChat("b", room.Id, admissions["b"], bBind) &&
            updates.SubscribeRoomChat("other", otherRoom.Id, admissions["other"], otherBind), "confirmed users subscribe to their own room");
        Check(!updates.PrepareRoomChat("a", room.Id, admissions["a"], "stale", "hi").Success, "room chat rejects old binding");
        Check(!updates.PrepareRoomChat("a", otherRoom.Id, admissions["a"], aBind, "hi").Success, "room chat rejects requested room mismatch");
        Check(!updates.PrepareRoomChat("a", room.Id, admissions["a"], aBind, new string('x', 201)).Success &&
            !updates.PrepareRoomChat("a", room.Id, admissions["a"], aBind, "x\ny").Success, "room chat enforces text validation");
        var sent = updates.PrepareRoomChat("a", room.Id, admissions["a"], aBind, "안녕하세요");
        Check(sent.Success && sent.Recipients.Select(r => r.ConnectionId).Order().SequenceEqual(new[] { "a", "b" }), "room chat excludes lobby-only reserved and other-room users");
        using (var payload = JsonDocument.Parse(sent.Payload!))
            Check(payload.RootElement.GetProperty("roomId").GetString() == room.Id && !sent.Payload!.Contains(admissions["a"]) &&
                payload.RootElement.GetProperty("userId").GetString() == members["a"].Id, "room chat supplies authoritative identity and excludes admission token");
        Check(sent.Recipients.Single(r => r.ConnectionId == "b").BindingId == bBind, "room chat includes recipient-specific binding generation");
        Check(!updates.PrepareChat("a", "cross-channel spam").Success, "lobby and room chat share user rate limit");
        room.IsRunning = true;
        Check(updates.PrepareRoomChat("b", room.Id, admissions["b"], bBind, "running").Success, "room chat works while room is running");
        room.IsRunning = false;
        rooms.ClientDisconnected(room.Id, "main", members["b"].Id, admissions["b"]);
        Check(!updates.PrepareRoomChat("b", room.Id, admissions["b"], bBind, "left").Success, "departed room participant cannot send");
        var next = await rooms.JoinRoom(new JoinRoomRequest { UserId = members["b"].Id, LobbyId = lobby.Id, RoomId = room.Id });
        rooms.ConfirmConnection(room.Id, "main", members["b"].Id, next.AdmissionToken);
        Check(!updates.SubscribeRoomChat("b", room.Id, admissions["b"], bBind), "reconnect requires fresh admission token");
        await Task.Delay(1100);
        var afterReconnect = updates.PrepareRoomChat("a", room.Id, admissions["a"], aBind, "fresh");
        Check(afterReconnect.Recipients.Count == 1, "reconnected participant old subscription cannot receive");
        var freshBind = Guid.NewGuid().ToString("N");
        Check(updates.SubscribeRoomChat("b", room.Id, next.AdmissionToken, freshBind), "reconnected participant binds fresh room chat");
        Check(!updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, bBind, "old").Success, "fresh admission cannot reuse old binding generation");
        room.IsClosing = true;
        Check(!updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, freshBind, "closing").Success, "closing room rejects chat immediately");
        room.IsClosing = false;
        Check(updates.SubscribeRoomChat("b", "", "", "") && !updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, freshBind, "removed").Success, "room chat unsubscription removes send permission");
        rooms.ServerDisconnected(room.Id, "main");
        Check(!updates.PrepareRoomChat("a", room.Id, admissions["a"], aBind, "reset").Success, "server reset revokes room chat");
        clock.Advance(TimeSpan.FromDays(1));
        Check(!updates.PrepareRoomChat("other", otherRoom.Id, admissions["other"], otherBind, "expired").Success, "expired auth token revokes room chat");
        updates.Remove("other");
        Check(!updates.SubscribeRoomChat("other", otherRoom.Id, admissions["other"], otherBind), "disconnected subscription cannot rebind room chat");
    }
}
