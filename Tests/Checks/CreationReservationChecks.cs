using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class CreationReservationChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        var clock = new TestClock();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 30 }), clock);
        var lobbies = TestCatalogLobbies.Create(users, "creation");
        var rooms = new RoomService(lobbies, users,
            Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 30, MinUsers = 1, MaxUsers = 4 }), timeProvider: clock);
        var login = users.Login(new LoginRequest { UserName = "creator" });
        var user = login.User!;
        await lobbies.JoinLobby(user.Id, new LobbyMembershipRequest { LobbyId = "creation" });
        CreateRoomRequest Request(string name) => new()
        {
            LobbyId = "creation", ContentId = "sample-training", RoomName = name, MaxUsers = 1
        };

        // 생성 응답을 클라이언트가 받지 못했다고 가정해 아무 후속 요청도 보내지 않습니다.
        var lost = rooms.CreateRoom(user.Id, Request("lost-response")).Room!;
        Check(lost.ReservedUsers == 1 && lost.State == ServerState.Creating && user.PendingRoomId == lost.Id && lost.Users.IsEmpty,
            "lost creation response still has expiring initial reservation without launched server");
        clock.Advance(TimeSpan.FromMinutes(10));
        rooms.ExpireReservations();
        Check(rooms.FindById(lost.Id) == null && user.PendingRoomId == "" && lobbies.FindById("creation")!.Rooms.IsEmpty,
            "unclaimed creation reservation expiry removes room from both stores");

        var cancelled = rooms.CreateRoom(user.Id, Request("cancelled")).Room!;
        Check(rooms.LeaveRoom(user.Id, new RoomMembershipRequest { LobbyId = "creation", RoomId = cancelled.Id }).Success && rooms.FindById(cancelled.Id) == null,
            "creation reservation can be cancelled before server startup");

        var room = rooms.CreateRoom(user.Id, Request("one-seat")).Room!;
        room.State = ServerState.Starting;
        room.ServerToken = "creation-fixture";
        rooms.RegisterServer(room.Id, "creation-fixture", "creation-server");
        var admission = await rooms.JoinRoom(user.Id, new JoinRoomRequest { LobbyId = "creation", RoomId = room.Id });
        Check(admission.Success && room.ReservedUsers == 1 && room.Users.IsEmpty,
            "one-seat creator claims existing reservation without double-counting capacity");
        Check(!(await rooms.JoinRoom(user.Id, new JoinRoomRequest { LobbyId = "creation", RoomId = room.Id })).Success,
            "duplicate join cannot reclaim creation reservation twice");
        Check(rooms.ConfirmConnection(room.Id, "creation-server", user.Id, admission.AdmissionToken).Success && room.ReservedUsers == 0 && room.Users.Count == 1 && room.MasterUserId == user.Id,
            "creation reservation becomes confirmed initial master only after transport confirmation");
        Check(rooms.LeaveRoom(user.Id, new RoomMembershipRequest { LobbyId = "creation", RoomId = room.Id }).Success && rooms.FindById(room.Id) == null,
            "last confirmed participant leaving removes room without creator metadata");
    }
}
