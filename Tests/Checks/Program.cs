using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new Exception(name);
    }

    Console.WriteLine("PASS: " + name);
}

// 포트 해제·중복 해제·동시 배정의 동작을 확인합니다.
var firstPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
var secondPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
Check(firstPort != secondPort, "active room ports are distinct");
Smart_Training_Server.Helper.PortHelper.ReleasePort(firstPort);
Smart_Training_Server.Helper.PortHelper.ReleasePort(firstPort);
var reusedPort = Smart_Training_Server.Helper.PortHelper.GetAvailablePort();
Check(reusedPort == firstPort, "released room port is reused");
var concurrentPorts = new System.Collections.Concurrent.ConcurrentBag<int>();
Parallel.For(0, 64, _ => concurrentPorts.Add(Smart_Training_Server.Helper.PortHelper.GetAvailablePort()));
Check(concurrentPorts.Distinct().Count() == 64 && !concurrentPorts.Contains(secondPort) && !concurrentPorts.Contains(reusedPort), "concurrent port allocations remain distinct");
foreach (var allocatedPort in concurrentPorts) Smart_Training_Server.Helper.PortHelper.ReleasePort(allocatedPort);
Smart_Training_Server.Helper.PortHelper.ReleasePort(secondPort);
Smart_Training_Server.Helper.PortHelper.ReleasePort(reusedPort);

var users = new UserService(Options.Create(new AuthOptions {
            NameMinLength = 1,
            NameMaxLength = 20
        }));
var lobbies = TestCatalogLobbies.Create(users, "first", "other", "waiting");
var rooms = new RoomService(
    lobbies,
    users,
    Options.Create(new RoomOptions {
            MinUsers = 1,
            MaxUsers = 10,
            NameMinLength = 1,
            NameMaxLength = 20
        })
);
var user = users.Login(new LoginRequest { UserName = "tester" }).User!;
var lobby = TestCatalogLobbies.Get(lobbies, "first");
var other = TestCatalogLobbies.Get(lobbies, "other");
Check((await lobbies.JoinLobby(user.Id, new LobbyMembershipRequest { LobbyId = lobby.Id })).Success, "lobby entry");
lobby.Contents["test"] = new ContentInfo
{
    RootContent = "test"
};
var room = rooms.CreateRoom(
    user.Id,
    new CreateRoomRequest {
        LobbyId = lobby.Id,
        RoomName = "test",
        ContentId = "test",
        MaxUsers = 2
    }
).Room!;
Check(room.ReservedUsers == 1 && user.PendingRoomId == room.Id && room.Users.IsEmpty,
    "creation atomically reserves initial master without claiming connection");
Check(!rooms.CreateRoom(user.Id, new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "duplicate-create", ContentId = "test", MaxUsers = 2 }).Success,
    "pending creator cannot create a second room");
// 자동 입장 실패 시 빈 방 정리 권한과 초기 방장 예약을 확인합니다.
var guest = users.Login(new LoginRequest { UserName = "auto-join-guest" }).User!;
await lobbies.JoinLobby(guest.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
Check(!rooms.LeaveRoom(guest.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id }).Success,
    "non-creator cannot remove unused created room");
Check(rooms.LeaveRoom(user.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id }).Success && rooms.FindById(room.Id) == null,
    "creator can clean empty room after automatic join failure");
room = rooms.CreateRoom(user.Id, new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "auto-join", ContentId = "test", MaxUsers = 2 }).Room!;
room.State = Smart_Training_Server.Enum.ServerState.Starting;
room.ServerToken = "auto-fixture";
Check(rooms.RegisterServer(room.Id, "auto-fixture", "auto-server"), "auto-join fixture server registers");
var creatorAdmission = await rooms.JoinRoom(user.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
var guestAdmission = await rooms.JoinRoom(guest.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
Check(creatorAdmission.Success && guestAdmission.Success, "creator and guest reserve independently");
Check(rooms.ConfirmConnection(room.Id, "auto-server", guest.Id, guestAdmission.AdmissionToken).Success && room.MasterUserId == user.Id,
    "guest connecting first preserves reserved creator master");
Check(rooms.ConfirmConnection(room.Id, "auto-server", user.Id, creatorAdmission.AdmissionToken).Success && room.MasterUserId == user.Id,
    "creator confirms as initial master");
rooms.LeaveRoom(guest.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id });
rooms.LeaveRoom(user.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id });
room = rooms.CreateRoom(user.Id, new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "auto-cancel", ContentId = "test", MaxUsers = 2 }).Room!;
room.State = Smart_Training_Server.Enum.ServerState.Starting;
room.ServerToken = "auto-fixture";
rooms.RegisterServer(room.Id, "auto-fixture", "auto-server");
creatorAdmission = await rooms.JoinRoom(user.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
guestAdmission = await rooms.JoinRoom(guest.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
rooms.ConfirmConnection(room.Id, "auto-server", guest.Id, guestAdmission.AdmissionToken);
Check(rooms.LeaveRoom(user.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id }).Success && room.MasterUserId == guest.Id && room.Users.ContainsKey(guest.Id),
    "cancelled creator admission transfers master and preserves guest");
rooms.LeaveRoom(guest.Id, new RoomMembershipRequest { LobbyId = lobby.Id, RoomId = room.Id });
room = TestRoomSetup.Create(rooms, user.Id, new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "test", ContentId = "test", MaxUsers = 2 }).Room!;
// Seed an established Netcode connection without launching a Unity executable.
room.Users[user.Id] = user;
user.CurrentRoomId = room.Id;
user.CurrentRoomName = room.Name;
user.RoomIndex = 0;
Check(
    !lobbies.LeaveLobby(user.Id, new LobbyMembershipRequest { LobbyId = lobby.Id }).Success,
    "block lobby exit while in room"
);
Check(
    !rooms.LeaveRoom(user.Id, new RoomMembershipRequest {
            LobbyId = other.Id,
            RoomId = room.Id
        }).Success,
    "reject mismatched lobby"
);
Check(room.Users.ContainsKey(user.Id) && user.CurrentRoomId == room.Id, "rejected exit preserves membership");
Check(
    rooms.LeaveRoom(user.Id, new RoomMembershipRequest {
            LobbyId = lobby.Id,
            RoomId = room.Id
        }).Success,
    "room exit"
);
Check(user.CurrentRoomId == "" && user.CurrentRoomName == "" && user.RoomIndex == -1, "reset user room state");
Check(rooms.FindById(room.Id) == null && !lobby.Rooms.ContainsKey(room.Id), "remove empty room from both lists");
Check(
    lobbies.LeaveLobby(user.Id, new LobbyMembershipRequest { LobbyId = lobby.Id }).Success,
    "lobby exit after room exit"
);
var clock = new TestClock();
var reservationUsers = new UserService(Options.Create(new AuthOptions {
            NameMinLength = 1,
            NameMaxLength = 20
        }));
var reservationLobbies = TestCatalogLobbies.Create(reservationUsers, "first", "other", "waiting");
var reservationRooms = new RoomService(
    reservationLobbies,
    reservationUsers,
    Options.Create(new RoomOptions {
            MinUsers = 1,
            MaxUsers = 10,
            NameMinLength = 1,
            NameMaxLength = 20,
            ServerExecutablePath = "missing-test-server.exe"
        }),
    clock
);
var waitingLobby = TestCatalogLobbies.Get(reservationLobbies, "waiting");
waitingLobby.Contents["test"] = new ContentInfo
{
    RootContent = "test"
};
async Task<UserInfo> AddUser(string name)
{
    var result = reservationUsers.Login(new LoginRequest { UserName = name }).User!;
    await reservationLobbies.JoinLobby(result.Id, new LobbyMembershipRequest { LobbyId = waitingLobby.Id });
    return result;
}

var firstUser = await AddUser("first");
var secondUser = await AddUser("second");
var thirdUser = await AddUser("third");
var pendingRoom = TestRoomSetup.Create(reservationRooms,
    firstUser.Id,
    new CreateRoomRequest {
        LobbyId = waitingLobby.Id,
        RoomName = "pending",
        ContentId = "test",
        MaxUsers = 2
    }
).Room!;
void Ready(RoomInfo target, string connection = "server")
{
    target.State = Smart_Training_Server.Enum.ServerState.Starting;
    target.ServerToken = "test-secret";
    Check(reservationRooms.RegisterServer(target.Id, "test-secret", connection), "register server with secret");
}

Task<Smart_Training_Server.Responses.Room.JoinRoomResponse> Reserve(UserInfo person, RoomInfo target) => reservationRooms.JoinRoom(person.Id, new JoinRoomRequest {
        LobbyId = waitingLobby.Id,
        RoomId = target.Id
    });
Ready(pendingRoom);
Check(
    !reservationRooms.GetRoomMaster(pendingRoom.Id, "imposter").Success,
    "unregistered server cannot read room authority"
);
Check(
    reservationRooms.GetRoomMaster(pendingRoom.Id, "server").MasterUserId == firstUser.Id,
    "room creation records creator as initial master"
);
Check(!reservationRooms.RegisterServer(pendingRoom.Id, "wrong", "imposter"), "reject false server secret");
Check(
    !reservationRooms.RegisterServer(pendingRoom.Id, "test-secret", "other-connection"),
    "reject second active server connection"
);
var admissions = await Task.WhenAll(
    Task.Run(() => Reserve(firstUser, pendingRoom)),
    Task.Run(() => Reserve(secondUser, pendingRoom)),
    Task.Run(() => Reserve(thirdUser, pendingRoom))
);
Check(
    admissions.Count(x => x.Success) == 2 && pendingRoom.ReservedUsers == 2,
    "concurrent reservations respect capacity"
);
Check(pendingRoom.Users.IsEmpty && firstUser.CurrentRoomId == "", "reservation does not establish membership");
var joined = new[]
{
    firstUser,
    secondUser,
    thirdUser
}.Where(x => x.PendingRoomId == pendingRoom.Id).ToArray();
var a = joined[0];
var b = joined[1];
var responseA = admissions[Array.IndexOf(new[] { firstUser, secondUser, thirdUser }, a)];
var responseB = admissions[Array.IndexOf(new[] { firstUser, secondUser, thirdUser }, b)];
Check(
    responseA.AdmissionToken.Length == 64 && responseA.AdmissionToken != responseB.AdmissionToken,
    "distinct random admission tokens"
);
Check(!(await Reserve(a, pendingRoom)).Success, "reject duplicate reservation");
Check(
    !reservationLobbies.LeaveLobby(a.Id, new LobbyMembershipRequest { LobbyId = waitingLobby.Id }).Success,
    "block lobby departure during reservation"
);
Check(
    !reservationRooms.ValidateConnection(pendingRoom.Id, "imposter", a.Id, responseA.AdmissionToken).Success,
    "only registered server validates"
);
Check(
    reservationRooms.ValidateConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && pendingRoom.Users.IsEmpty,
    "approval validation does not consume reservation"
);
Check(
    !reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, "wrong-token").Success,
    "reject wrong admission token"
);
Check(
    !reservationRooms.ConfirmConnection(pendingRoom.Id, "server", b.Id, responseA.AdmissionToken).Success,
    "token bound to user"
);
Check(
    reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success,
    "confirm actual connection"
);
Check(
    reservationRooms.GetRoomMaster(pendingRoom.Id, "server").MasterUserId == a.Id,
    "first confirmed participant becomes active master when creator is absent"
);
var identity = reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken) as Smart_Training_Server.Responses.Room.RoomParticipantResponse;
Check(
    identity != null && identity.UserId == a.Id && identity.UserName == a.Name && identity.RoomIndex == a.RoomIndex,
    "confirmation returns authenticated participant identity and room slot"
);
Check(
    pendingRoom.Users.Count == 1 && pendingRoom.ReservedUsers == 1 && a.CurrentRoomId == pendingRoom.Id && a.PendingRoomId == "",
    "confirmation updates both states"
);
Check(
    reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && pendingRoom.Users.Count == 1,
    "idempotent confirmation"
);
Check(
    !reservationRooms.ClientDisconnected(pendingRoom.Id, "server", a.Id, "old-token").Success && a.CurrentRoomId == pendingRoom.Id,
    "stale disconnect cannot remove current connection"
);
Check(
    reservationRooms.ClientDisconnected(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && reservationRooms.FindById(pendingRoom.Id) != null,
    "keep room while another reservation exists"
);
clock.Advance(TimeSpan.FromSeconds(31));
reservationRooms.ExpireReservations();
Check(b.PendingRoomId == "" && pendingRoom.ReservedUsers == 0, "expire reservation without client notification");
Check(
    !reservationRooms.ConfirmConnection(pendingRoom.Id, "server", b.Id, responseB.AdmissionToken).Success,
    "reject expired token"
);
Check(reservationRooms.FindById(pendingRoom.Id) == null, "expired last admission removes empty room");
pendingRoom = TestRoomSetup.Create(reservationRooms, b.Id, new CreateRoomRequest { LobbyId = waitingLobby.Id, RoomName = "pending", ContentId = "test", MaxUsers = 2 }).Room!;
Ready(pendingRoom, "new-server");
var retry = await Reserve(b, pendingRoom);
Check(retry.Success, "retry after expiry");
Check(
    reservationRooms.LeaveRoom(b.Id, new RoomMembershipRequest {
            LobbyId = waitingLobby.Id,
            RoomId = pendingRoom.Id
        }).Success && b.PendingRoomId == "",
    "cancel pending reservation via leave API"
);
pendingRoom = TestRoomSetup.Create(reservationRooms, a.Id, new CreateRoomRequest { LobbyId = waitingLobby.Id, RoomName = "pending", ContentId = "test", MaxUsers = 2 }).Room!;
Ready(pendingRoom, "last-server");
var finalReservation = await Reserve(a, pendingRoom);
Check(
    reservationRooms.ConfirmConnection(pendingRoom.Id, "last-server", a.Id, finalReservation.AdmissionToken).Success,
    "reconnect with fresh token"
);
reservationRooms.ServerDisconnected(pendingRoom.Id, "new-server");
Check(
    !reservationRooms.GetRoomMaster(pendingRoom.Id, "new-server").Success,
    "stale server connection cannot query current room authority"
);
Check(a.CurrentRoomId == pendingRoom.Id, "ignore stale server disconnect");
Check(
    reservationRooms.SetRoomEntryLock(
        a.Id,
        new SetRoomEntryLockRequest {
            LobbyId = waitingLobby.Id,
            RoomId = pendingRoom.Id,
            IsEntryLocked = true
        }
    ).Success,
    "lock room before dedicated server reset"
);
reservationRooms.ServerDisconnected(pendingRoom.Id, "last-server");
Check(!pendingRoom.IsEntryLocked, "dedicated server reset clears lock so room can restart without master");
Check(a.CurrentRoomId == "" && pendingRoom.Users.IsEmpty, "clear membership when active server disconnects");
var json = System.Text.Json.JsonSerializer.Serialize(pendingRoom);
Check(!json.Contains("test-secret") && !json.Contains("ServerToken"), "server secret excluded from JSON");
pendingRoom = TestRoomSetup.Create(reservationRooms, a.Id, new CreateRoomRequest { LobbyId = waitingLobby.Id, RoomName = "pending", ContentId = "test", MaxUsers = 2 }).Room!;
var failed = await Reserve(a, pendingRoom); // No Unity executable exists in the test build.
Check(
    !failed.Success && a.PendingRoomId == "" && pendingRoom.ReservedUsers == 0,
    "release reservation after server launch failure"
);
await CreationReservationChecks.Run();
ConfigurationChecks.Run();
await CatalogLobbyChecks.Run();
await OperationsChecks.Run();
await OperationsHistoryChecks.Run();
await MaintenanceChecks.Run();
await ShutdownChecks.Run();
await OperatingModeChecks.Run();
await OperatingModeApiChecks.Run();
await SettingsChecks.Run();
await ConcurrentRoomChecks.Run();
await JoinCancellationChecks.Run();
await LobbyUpdatesChecks.Run();
await LobbyGroupBroadcastChecks.Run();
await LobbyChatChecks.Run();
await RoomChatChecks.Run();
await LobbyParticipantsChecks.Run();
await CloseRoomChecks.Run();
await PasswordRoomChecks.Run();
await ContentChecks.Run();
await QueryChecks.Run();
await AuthChecks.Run();
await LogoutChecks.Run();
await ServerProcessChecks.Run();
ResponseContractChecks.Run();
await ApiContractChecks.Run();
sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan amount) => now += amount;
}
