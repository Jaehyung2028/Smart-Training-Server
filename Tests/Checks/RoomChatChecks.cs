using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class RoomChatChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        var clock = new TestClock();
        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }), clock);
        var lobbies = TestCatalogLobbies.Create(users, "chat");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 8
                })
        );
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var groups = new TestSignalRGroups();
        var lobby = TestCatalogLobbies.Get(lobbies, "chat");
        lobby.Contents["test"] = new ContentInfo
        {
            RootContent = "test"
        };
        var ids = new[]
        {
            "a",
            "b",
            "pending",
            "other"
        };
        var members = new Dictionary<string, UserInfo>();
        var tokens = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            var login = users.Login(new LoginRequest { UserName = id });
            members[id] = login.User!;
            await lobbies.JoinLobby(login.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            updates.Subscribe(id, lobby.Id, "Bearer " + login.AccessToken);
            tokens[id] = login.AccessToken!;
        }

        RoomInfo Create(string name)
        {
            var room = TestRoomSetup.Create(rooms,
                members["a"].Id,
                new CreateRoomRequest {
                    LobbyId = lobby.Id,
                    RoomName = name,
                    ContentId = "test",
                    MaxUsers = 4
                }
            ).Room!;
            room.State = Smart_Training_Server.Enum.ServerState.Starting;
            room.ServerToken = "fixture";
            rooms.RegisterServer(room.Id, "fixture", name);
            return room;
        }

        var room = Create("main");
        var otherRoom = Create("other-room");
        var admissions = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            var target = id == "other" ? otherRoom : room;
            var admission = await rooms.JoinRoom(members[id].Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = target.Id
                });
            admissions[id] = admission.AdmissionToken;
            if (id != "pending")
            {
                rooms.ConfirmConnection(target.Id, target == room ? "main" : "other-room", members[id].Id, admission.AdmissionToken);
            }
        }

        Check(
            !updates.SubscribeRoomChat("unknown", room.Id, admissions["a"]).Success,
            "room chat requires authenticated lobby subscription"
        );
        Check(
            !updates.SubscribeRoomChat("pending", room.Id, admissions["pending"]).Success,
            "reservation alone cannot subscribe to room chat"
        );
        Check(
            !updates.SubscribeRoomChat("other", room.Id, admissions["other"]).Success,
            "other room participant cannot subscribe"
        );
        Check(
            !updates.SubscribeRoomChat("a", room.Id, "stale").Success,
            "room chat rejects stale admission"
        );
        Check(
            updates.SubscribeRoomChat("a", room.Id, admissions["a"]).Success && updates.SubscribeRoomChat("b", room.Id, admissions["b"]).Success && updates.SubscribeRoomChat("other", otherRoom.Id, admissions["other"]).Success,
            "confirmed users subscribe to their own room"
        );
        Check(
            !updates.PrepareRoomChat("a", room.Id, "stale", "hi").Success,
            "room chat rejects stale send token"
        );
        Check(
            !updates.PrepareRoomChat("a", otherRoom.Id, admissions["a"], "hi").Success,
            "room chat rejects requested room mismatch"
        );
        Check(
            !updates.PrepareRoomChat("a", room.Id, admissions["a"], new string ('x', 201)).Success && !updates.PrepareRoomChat("a", room.Id, admissions["a"], "x\ny").Success,
            "room chat enforces text validation"
        );
        await groups.Synchronize(updates);
        var sent = updates.PrepareRoomChat("a", room.Id, admissions["a"], "안녕하세요");
        Check(
            sent.Success && groups.Members(sent.GroupName).SequenceEqual(new[] { "a", "b" }),
            "room chat excludes lobby-only reserved and other-room users"
        );
        using (var payload = JsonDocument.Parse(sent.Payload!))
        {
            Check(
                payload.RootElement.GetProperty("roomId").GetString() == room.Id && !sent.Payload!.Contains(admissions["a"]) && payload.RootElement.GetProperty("userId").GetString() == members["a"].Id,
                "room chat supplies authoritative identity and excludes admission token"
            );
        }

        Check(
            JsonDocument.Parse(sent.Payload!).RootElement.GetProperty("sequence").GetInt64() > 0,
            "room group messages carry a server sequence instead of recipient binding"
        );
        Check(!updates.PrepareChat("a", "cross-channel spam").Success, "lobby and room chat share user rate limit");
        room.IsRunning = true;
        Check(
            updates.PrepareRoomChat("b", room.Id, admissions["b"], "running").Success,
            "room chat works while room is running"
        );
        room.IsRunning = false;
        rooms.ClientDisconnected(room.Id, "main", members["b"].Id, admissions["b"]);
        Check(
            !updates.PrepareRoomChat("b", room.Id, admissions["b"], "left").Success,
            "departed room participant cannot send"
        );
        var next = await rooms.JoinRoom(members["b"].Id, new JoinRoomRequest {
                LobbyId = lobby.Id,
                RoomId = room.Id
            });
        rooms.ConfirmConnection(room.Id, "main", members["b"].Id, next.AdmissionToken);
        Check(
            !updates.SubscribeRoomChat("b", room.Id, admissions["b"]).Success,
            "reconnect requires fresh admission token"
        );
        await Task.Delay(1100);
        await groups.Synchronize(updates);
        var afterReconnect = updates.PrepareRoomChat("a", room.Id, admissions["a"], "fresh");
        Check(groups.Members(afterReconnect.GroupName).Length == 1, "reconnected participant old subscription cannot receive");
        var rebound = updates.SubscribeRoomChat("b", room.Id, next.AdmissionToken);
        Check(rebound.Success, "reconnected participant binds fresh room chat");
        await groups.Synchronize(updates);
        Check(groups.Members(LobbyRoomUpdates.GetRoomGroupName(room.Id)).SequenceEqual(new[] { "a", "b" }), "fresh room subscription rejoins room group");
        using (var oldMessage = JsonDocument.Parse(afterReconnect.Payload!))
        {
            Check(rebound.ChatSequence >= oldMessage.RootElement.GetProperty("sequence").GetInt64(), "new subscription watermark excludes previously created room messages");
        }

        var freshMessage = updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, "new subscription");
        using (var message = JsonDocument.Parse(freshMessage.Payload!))
        {
            Check(freshMessage.Success && message.RootElement.GetProperty("sequence").GetInt64() > rebound.ChatSequence, "new group message is newer than subscription watermark");
        }
        Check(
            !updates.PrepareRoomChat("b", room.Id, admissions["b"], "old").Success,
            "old admission cannot send after fresh subscription"
        );
        room.IsClosing = true;
        Check(
            !updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, "closing").Success,
            "closing room rejects chat immediately"
        );
        room.IsClosing = false;
        Check(
            updates.SubscribeRoomChat("b", "", "").Success && !updates.PrepareRoomChat("b", room.Id, next.AdmissionToken, "removed").Success,
            "room chat unsubscription removes send permission"
        );
        rooms.ServerDisconnected(room.Id, "main");
        Check(
            !updates.PrepareRoomChat("a", room.Id, admissions["a"], "reset").Success,
            "server reset revokes room chat"
        );
        clock.Advance(TimeSpan.FromDays(1));
        Check(
            !updates.PrepareRoomChat("other", otherRoom.Id, admissions["other"], "expired").Success,
            "expired auth token revokes room chat"
        );
        await groups.Synchronize(updates);
        Check(groups.Members(LobbyRoomUpdates.GetRoomGroupName(room.Id)).Length == 0 && groups.Members(LobbyRoomUpdates.GetRoomGroupName(otherRoom.Id)).Length == 0, "reset and expired sessions leave room groups");
        updates.Remove("other");
        Check(
            !updates.SubscribeRoomChat("other", otherRoom.Id, admissions["other"]).Success,
            "disconnected subscription cannot rebind room chat"
        );
    }
}
