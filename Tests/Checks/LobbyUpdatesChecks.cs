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

internal static class LobbyUpdatesChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 10 }));
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var login = users.Login(new LoginRequest { UserName = "watcher" });
        var user = login.User!;
        var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "watch" }).Lobby!;
        var foreign = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "foreign" }).Lobby!;
        lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
        await lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = lobby.Id });
        Check(!updates.Subscribe("bad", lobby.Id, "Bearer invalid"), "live list rejects invalid token");
        Check(!updates.Subscribe("bad", foreign.Id, "Bearer " + login.AccessToken), "live list rejects foreign lobby");
        Check(updates.Subscribe("watcher", lobby.Id, "Bearer " + login.AccessToken), "live list subscribes authenticated lobby member");
        Check(updates.CollectChanges().Count == 1 && updates.CollectChanges().Count == 0, "live list sends initial invalidation and suppresses unchanged snapshots");
        var room = rooms.CreateRoom(new CreateRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomName = "initial", ContentId = "test", MaxUsers = 4 }).Room!;
        Check(updates.CollectChanges().Count == 1, "live list detects room creation");
        room.Name = "renamed"; room.MaxUsers = 5;
        Check(updates.CollectChanges().Count == 1 && updates.CollectChanges().Count == 0, "live list coalesces settings changes");
        room.IsEntryLocked = true; room.IsRunning = true;
        Check(updates.CollectChanges().Count == 1, "live list detects entry lock and phase");
        room.ReservedUsers = 1;
        Check(updates.CollectChanges().Count == 1, "live list detects reserved capacity");
        room.Users[user.Id] = user; room.MasterUserId = user.Id;
        Check(updates.CollectChanges().Count == 1, "live list detects membership and master");
        lobby.Rooms.TryRemove(room.Id, out _);
        Check(updates.CollectChanges().Count == 1, "live list detects room deletion");
        user.CurrentLobbyId = foreign.Id;
        var ended = updates.CollectChanges();
        Check(ended.Count == 1 && ended[0].Ended && updates.CollectChanges().Count == 0, "lobby departure terminates subscription before further updates");
        user.CurrentLobbyId = lobby.Id;
        updates.Subscribe("watcher", lobby.Id, "Bearer " + login.AccessToken);
        users.RefreshSession(login.AccessToken);
        Check(updates.CollectChanges().Single().Ended, "token rotation terminates old subscription");
        updates.Remove("watcher");
        Check(updates.CollectChanges().Count == 0, "disconnected subscription is removed");
        var refreshed = users.Login(new LoginRequest { UserName = "globalwatch" });
        Check(updates.Subscribe("global", "", "Bearer " + refreshed.AccessToken), "logged in user subscribes lobby counts before joining any lobby");
        updates.CollectChanges();
        foreign.Users[refreshed.User!.Id] = refreshed.User;
        Check(updates.CollectChanges().Count == 1, "global lobby list detects participant count change");
        foreign.Rooms[room.Id] = room;
        Check(updates.CollectChanges().Count == 1, "global lobby list detects room count change in another lobby");
        Check(updates.CollectChanges().Count == 0, "global lobby list suppresses duplicate updates");
        lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "newlobby" });
        Check(updates.CollectChanges().Count == 1, "global lobby list detects newly created lobby");
    }
}

