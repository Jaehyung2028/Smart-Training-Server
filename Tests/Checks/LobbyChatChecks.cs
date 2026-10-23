using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class LobbyChatChecks
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

        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.Create(users, "chat", "other");
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions()));
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var groups = new TestSignalRGroups();
        var a = users.Login(new LoginRequest { UserName = "가나다" });
        var b = users.Login(new LoginRequest { UserName = "second" });
        var c = users.Login(new LoginRequest { UserName = "foreign" });
        var d = users.Login(new LoginRequest { UserName = "global" });
        var lobby = TestCatalogLobbies.Get(lobbies, "chat");
        var other = TestCatalogLobbies.Get(lobbies, "other");
        foreach (var login in new[]
        {
            a,
            b
        }

        )
        {
            await lobbies.JoinLobby(login.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        }

        await lobbies.JoinLobby(c.User!.Id, new LobbyMembershipRequest { LobbyId = other.Id });
        updates.Subscribe("a", lobby.Id, "Bearer " + a.AccessToken);
        updates.Subscribe("a2", lobby.Id, "Bearer " + a.AccessToken);
        updates.Subscribe("b", lobby.Id, "Bearer " + b.AccessToken);
        updates.Subscribe("c", other.Id, "Bearer " + c.AccessToken);
        updates.Subscribe("d", "", "Bearer " + d.AccessToken);
        Check(!updates.PrepareChat("unknown", "hi").Success, "chat rejects unsubscribed connection");
        Check(!updates.PrepareChat("d", "hi").Success, "chat rejects global-only subscription");
        Check(
            !updates.PrepareChat("a", null).Success && !updates.PrepareChat("a", " \t ").Success,
            "chat rejects empty messages"
        );
        Check(!updates.PrepareChat("a", new string ('x', 201)).Success, "chat enforces 200 character limit");
        Check(
            !updates.PrepareChat("a", "line\nbreak").Success && !updates.PrepareChat("a", "x\0y").Success,
            "chat rejects control characters"
        );
        await groups.Synchronize(updates);
        var sent = updates.PrepareChat("a", "  안녕하세요🙂  ");
        Check(
            sent.Success && groups.Members(sent.GroupName).SequenceEqual(new[] { "a", "a2", "b" }),
            "chat targets same lobby only including sender"
        );
        using (var doc = JsonDocument.Parse(sent.Payload!))
        {
            var json = doc.RootElement;
            Check(
                json.GetProperty("userId").GetString() == a.User!.Id && json.GetProperty("nickname").GetString() == "가나다",
                "chat identity comes from authenticated server user"
            );
            Check(
                json.GetProperty("text").GetString() == "안녕하세요🙂" && json.GetProperty("lobbyId").GetString() == lobby.Id,
                "chat preserves unicode and trims surrounding whitespace"
            );
            Check(
                DateTimeOffset.TryParse(json.GetProperty("sentAt").GetString(), out _) && !string.IsNullOrEmpty(json.GetProperty("id").GetString()),
                "chat supplies server timestamp and message id"
            );
            Check(!sent.Payload!.Contains(a.AccessToken!), "chat payload excludes credentials");
        }

        Check(!updates.PrepareChat("a2", "spam").Success, "chat rate limit applies across user connections");
        a.User!.CurrentLobbyId = other.Id;
        await groups.Synchronize(updates);
        var reply = updates.PrepareChat("b", new string ('x', 200));
        Check(
            reply.Success && groups.Members(reply.GroupName).SequenceEqual(new[] { "b" }),
            "chat excludes departed recipient before cleanup tick and accepts 200 characters"
        );
        Check(!updates.PrepareChat("a", "stale").Success, "chat rejects departed sender before cleanup tick");
        a.User.CurrentLobbyId = lobby.Id;
        users.RefreshSession(a.AccessToken!);
        Check(!updates.PrepareChat("a", "stale token").Success, "chat rejects rotated sender token");
        await Task.Delay(1100);
        await groups.Synchronize(updates);
        var next = updates.PrepareChat("b", "next");
        Check(
            next.Success && groups.Members(next.GroupName).SequenceEqual(new[] { "b" }),
            "chat resumes after cooldown and excludes rotated recipient tokens"
        );
        updates.Remove("b");
        await groups.Synchronize(updates);
        Check(!updates.PrepareChat("b", "offline").Success, "chat rejects removed connection");
        Check(groups.Members(LobbyRoomUpdates.GetGroupName(lobby.Id)).Length == 0, "removed or rotated connections leave lobby group");
        c.User!.CurrentLobbyId = lobby.Id;
        Check(updates.Subscribe("c", lobby.Id, "Bearer " + c.AccessToken), "group accepts valid lobby switch");
        await groups.Synchronize(updates);
        Check(groups.Members(LobbyRoomUpdates.GetGroupName(other.Id)).Length == 0 && groups.Members(LobbyRoomUpdates.GetGroupName(lobby.Id)).SequenceEqual(new[] { "c" }), "lobby switch removes old group and joins new group");
        Check(groups.Members(LobbyRoomUpdates.GetGroupName("")).SequenceEqual(new[] { "d" }), "catalog group stays isolated from lobby chat");
    }
}
