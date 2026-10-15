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

internal static class CloseRoomChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        foreach (bool failSend in new[] { false, true })
        {
            var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
            var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
            var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 8 }));
            var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "close" }).Lobby!;
            lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
            var members = new List<UserInfo>();
            foreach (var name in new[] { "master", "member", "pending" })
            {
                var user = users.Login(new LoginRequest { UserName = name }).User!;
                await lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = lobby.Id }); members.Add(user);
            }
            var room = rooms.CreateRoom(new CreateRoomRequest { UserId = members[0].Id, LobbyId = lobby.Id, RoomName = "room", ContentId = "test", MaxUsers = 4 }).Room!;
            room.State = Smart_Training_Server.Enum.ServerState.Starting; room.ServerToken = "fixture";
            rooms.RegisterServer(room.Id, "fixture", "close-server");
            var admissions = new List<string>();
            foreach (var user in members)
            {
                var admission = await rooms.JoinRoom(new JoinRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomId = room.Id }); admissions.Add(admission.AdmissionToken);
                if (user != members[2]) rooms.ConfirmConnection(room.Id, "close-server", user.Id, admission.AdmissionToken);
            }
            var hub = new RecordingRoomHub { FailSend = failSend };
            var request = new CloseRoomRequest { LobbyId = lobby.Id, RoomId = room.Id };
            Check(!(await rooms.CloseRoomAsync(members[1].Id, request, hub)).Success && room.Users.Count == 2, "ordinary participant cannot close room");
            Check(!(await rooms.CloseRoomAsync(members[2].Id, request, hub)).Success, "reservation cannot close room");
            Check(!(await rooms.CloseRoomAsync(members[0].Id, new() { LobbyId = "wrong", RoomId = room.Id }, hub)).Success, "close rejects mismatched lobby");
            room.IsRunning = true;
            var closing = rooms.CloseRoomAsync(members[0].Id, request, hub);
            Check(members.All(u => u.CurrentRoomId == "" && u.PendingRoomId == "" && u.CurrentLobbyId == lobby.Id), "close clears connected and reserved room membership but preserves lobby");
            Check(!(await rooms.JoinRoom(new() { UserId = members[2].Id, LobbyId = lobby.Id, RoomId = room.Id })).Success, "close rejects overlapping admission");
            Check(!(await rooms.CloseRoomAsync(members[0].Id, request, hub)).Success, "duplicate close cannot start another operation");
            Check((await closing).Success && rooms.FindById(room.Id) == null && !lobby.Rooms.ContainsKey(room.Id), "close removes room even when notification delivery fails");
            Check(!rooms.ConfirmConnection(room.Id, "close-server", members[2].Id, admissions[2]).Success, "close rejects delayed reservation confirmation");
            Check(room.Users.IsEmpty && room.ReservedUsers == 0 && !room.IsRunning && room.MasterUserId == "", "close clears all phase and authority state");
            if (!failSend) Check(hub.Messages.Single().Method == "CloseRoom", "close targets registered dedicated process");
        }
    }
}
