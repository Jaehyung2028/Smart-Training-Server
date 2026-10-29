using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class LobbyUpdatesChecks
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

        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = TestCatalogLobbies.Create(users, "watch", "foreign", "newlobby");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 10 })
        );
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var login = users.Login(new LoginRequest { UserName = "watcher" });
        var user = login.User!;
        var lobby = TestCatalogLobbies.Get(lobbies, "watch");
        var foreign = TestCatalogLobbies.Get(lobbies, "foreign");
        lobby.Contents["test"] = new ContentInfo
        {
            RootContent = "test"
        };
        await lobbies.JoinLobby(user.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        Check(
            !updates.Subscribe("bad", lobby.Id, "Bearer invalid"),
            "live list rejects invalid token"
        );
        Check(
            !updates.Subscribe("bad", foreign.Id, "Bearer " + login.AccessToken),
            "live list rejects foreign lobby"
        );
        Check(
            updates.Subscribe("watcher", lobby.Id, "Bearer " + login.AccessToken),
            "live list subscribes authenticated lobby member"
        );
        Check(
            updates.CollectChanges().Count == 1 && updates.CollectChanges().Count == 0,
            "live list sends initial invalidation and suppresses unchanged snapshots"
        );
        var room = TestRoomSetup.Create(
            rooms,
            user.Id,
            new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "initial", ContentId = "test", MaxUsers = 4 }
        ).Room!;
        Check(updates.CollectChanges().Count == 0, "live list hides room until master connects");
        room.Users[user.Id] = user;
        Check(updates.CollectChanges().Count == 1, "live list publishes connected master room");
        room.Name = "renamed";
        room.MaxUsers = 5;
        Check(
            updates.CollectChanges().Count == 1 && updates.CollectChanges().Count == 0,
            "live list coalesces settings changes"
        );
        room.IsEntryLocked = true;
        room.IsRunning = true;
        Check(updates.CollectChanges().Count == 1, "live list detects entry lock and phase");
        room.ReservedUsers = 1;
        Check(updates.CollectChanges().Count == 1, "live list detects reserved capacity");
        room.Users[user.Id] = user;
        var joined = users.Login(new LoginRequest { UserName = "list-guest" }).User!;
        room.Users[joined.Id] = joined;
        room.MasterUserId = joined.Id;
        Check(updates.CollectChanges().Count == 1, "live list detects membership and master");
        lobby.Rooms.TryRemove(room.Id, out _);
        Check(updates.CollectChanges().Count == 1, "live list detects room deletion");
        user.CurrentLobbyId = foreign.Id;
        var ended = updates.CollectChanges();
        Check(
            ended.Count == 1 && ended[0].Ended && updates.CollectChanges().Count == 0,
            "lobby departure terminates subscription before further updates"
        );
        user.CurrentLobbyId = lobby.Id;
        updates.Subscribe("watcher", lobby.Id, "Bearer " + login.AccessToken);
        users.RefreshSession(login.AccessToken);
        Check(
            updates.CollectChanges().Single().Ended,
            "token rotation terminates old subscription"
        );
        updates.Remove("watcher");
        Check(updates.CollectChanges().Count == 0, "disconnected subscription is removed");
        var refreshed = users.Login(new LoginRequest { UserName = "globalwatch" });
        Check(
            updates.Subscribe("global", "", "Bearer " + refreshed.AccessToken),
            "logged in user subscribes lobby counts before joining any lobby"
        );
        updates.CollectChanges();
        foreign.Users[refreshed.User!.Id] = refreshed.User;
        Check(
            updates.CollectChanges().Count == 1,
            "global lobby list detects participant count change"
        );
        foreign.Rooms[room.Id] = room;
        Check(
            updates.CollectChanges().Count == 1,
            "global lobby list detects room count change in another lobby"
        );
        Check(
            updates.CollectChanges().Count == 0,
            "global lobby list suppresses duplicate updates"
        );
        TestCatalogLobbies.Get(lobbies, "newlobby").Name = "renamed catalog lobby";
        Check(
            updates.CollectChanges().Count == 1,
            "global lobby list detects catalog metadata change"
        );
        updates.Subscribe("global-two", "", "Bearer " + refreshed.AccessToken);
        var initial = updates.CollectChanges();
        Check(
            initial.Count == 1 && initial[0].ConnectionId == "global-two",
            "new subscriber receives initial update even when shared snapshot is unchanged"
        );
        Check(
            updates.CollectChanges().Count == 0,
            "shared record snapshots suppress unchanged updates for multiple subscribers"
        );
    }
}
