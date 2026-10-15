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

void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { MaxCount = 10, NameMinLength = 1, NameMaxLength = 20 }));
var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 10, NameMinLength = 1, NameMaxLength = 20 }));
var user = users.Login(new LoginRequest { UserName = "tester" }).User!;
var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "first" }).Lobby!;
var other = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "other" }).Lobby!;
Check((await lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = lobby.Id })).Success, "lobby entry");
lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
var room = rooms.CreateRoom(new CreateRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomName = "test", ContentId = "test", MaxUsers = 2 }).Room!;
// Seed an established Netcode connection without launching a Unity executable.
room.Users[user.Id] = user;
user.CurrentRoomId = room.Id; user.CurrentRoomName = room.Name; user.RoomIndex = 0;
Check(!lobbies.LeaveLobby(new LeaveLobbyRequest { UserId = user.Id, LobbyId = lobby.Id }).Success, "block lobby exit while in room");
Check(!rooms.LeaveRoom(new LeaveRoomRequest { UserId = user.Id, LobbyId = other.Id, RoomId = room.Id }).Success, "reject mismatched lobby");
Check(room.Users.ContainsKey(user.Id) && user.CurrentRoomId == room.Id, "rejected exit preserves membership");
Check(rooms.LeaveRoom(new LeaveRoomRequest { UserId = user.Id, LobbyId = lobby.Id, RoomId = room.Id }).Success, "room exit");
Check(user.CurrentRoomId == "" && user.CurrentRoomName == "" && user.RoomIndex == -1, "reset user room state");
Check(rooms.FindById(room.Id) == null && !lobby.Rooms.ContainsKey(room.Id), "remove empty room from both lists");
Check(lobbies.LeaveLobby(new LeaveLobbyRequest { UserId = user.Id, LobbyId = lobby.Id }).Success, "lobby exit after room exit");

var clock = new TestClock();
var reservationUsers = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
var reservationLobbies = new LobbyService(new ContentService(), reservationUsers, Options.Create(new LobbyOptions { MaxCount = 10, NameMinLength = 1, NameMaxLength = 20 }));
var reservationRooms = new RoomService(reservationLobbies, reservationUsers, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 10, NameMinLength = 1, NameMaxLength = 20, ServerExecutablePath = "missing-test-server.exe" }), clock);
var waitingLobby = reservationLobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "waiting" }).Lobby!;
waitingLobby.Contents["test"] = new ContentInfo { RootContent = "test" };
async Task<UserInfo> AddUser(string name)
{
    var result = reservationUsers.Login(new LoginRequest { UserName = name }).User!;
    await reservationLobbies.JoinLobby(new JoinLobbyRequest { UserId = result.Id, LobbyId = waitingLobby.Id });
    return result;
}
var firstUser = await AddUser("first");
var secondUser = await AddUser("second");
var thirdUser = await AddUser("third");
var pendingRoom = reservationRooms.CreateRoom(new CreateRoomRequest { UserId = firstUser.Id, LobbyId = waitingLobby.Id, RoomName = "pending", ContentId = "test", MaxUsers = 2 }).Room!;
void Ready(RoomInfo target, string connection = "server")
{
    target.State = Smart_Training_Server.Enum.ServerState.Starting;
    target.ServerToken = "test-secret";
    Check(reservationRooms.RegisterServer(target.Id, "test-secret", connection), "register server with secret");
}
Task<Smart_Training_Server.Responses.Room.JoinRoomResponse> Reserve(UserInfo person, RoomInfo target) => reservationRooms.JoinRoom(new JoinRoomRequest { UserId = person.Id, LobbyId = waitingLobby.Id, RoomId = target.Id });
Ready(pendingRoom);
Check(!reservationRooms.GetRoomMaster(pendingRoom.Id, "imposter").Success, "unregistered server cannot read room authority");
Check(reservationRooms.GetRoomMaster(pendingRoom.Id, "server").MasterUserId == firstUser.Id,
    "room creation records creator as initial master");
Check(!reservationRooms.RegisterServer(pendingRoom.Id, "wrong", "imposter"), "reject false server secret");
Check(!reservationRooms.RegisterServer(pendingRoom.Id, "test-secret", "other-connection"), "reject second active server connection");
var admissions = await Task.WhenAll(Task.Run(() => Reserve(firstUser, pendingRoom)), Task.Run(() => Reserve(secondUser, pendingRoom)), Task.Run(() => Reserve(thirdUser, pendingRoom)));
Check(admissions.Count(x => x.Success) == 2 && pendingRoom.ReservedUsers == 2, "concurrent reservations respect capacity");
Check(pendingRoom.Users.IsEmpty && firstUser.CurrentRoomId == "", "reservation does not establish membership");
var joined = new[] { firstUser, secondUser, thirdUser }.Where(x => x.PendingRoomId == pendingRoom.Id).ToArray();
var a = joined[0]; var b = joined[1];
var responseA = admissions[Array.IndexOf(new[] { firstUser, secondUser, thirdUser }, a)];
var responseB = admissions[Array.IndexOf(new[] { firstUser, secondUser, thirdUser }, b)];
Check(responseA.AdmissionToken.Length == 64 && responseA.AdmissionToken != responseB.AdmissionToken, "distinct random admission tokens");
Check(!(await Reserve(a, pendingRoom)).Success, "reject duplicate reservation");
Check(!reservationLobbies.LeaveLobby(new LeaveLobbyRequest { UserId = a.Id, LobbyId = waitingLobby.Id }).Success, "block lobby departure during reservation");
Check(!reservationRooms.ValidateConnection(pendingRoom.Id, "imposter", a.Id, responseA.AdmissionToken).Success, "only registered server validates");
Check(reservationRooms.ValidateConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && pendingRoom.Users.IsEmpty, "approval validation does not consume reservation");
Check(!reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, "wrong-token").Success, "reject wrong admission token");
Check(!reservationRooms.ConfirmConnection(pendingRoom.Id, "server", b.Id, responseA.AdmissionToken).Success, "token bound to user");
Check(reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success, "confirm actual connection");
Check(reservationRooms.GetRoomMaster(pendingRoom.Id, "server").MasterUserId == a.Id,
    "first confirmed participant becomes active master when creator is absent");
var identity = reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken) as Smart_Training_Server.Responses.Room.RoomParticipantResponse;
Check(identity != null && identity.UserId == a.Id && identity.UserName == a.Name && identity.RoomIndex == a.RoomIndex,
    "confirmation returns authenticated participant identity and room slot");
Check(pendingRoom.Users.Count == 1 && pendingRoom.ReservedUsers == 1 && a.CurrentRoomId == pendingRoom.Id && a.PendingRoomId == "", "confirmation updates both states");
Check(reservationRooms.ConfirmConnection(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && pendingRoom.Users.Count == 1, "idempotent confirmation");
Check(!reservationRooms.ClientDisconnected(pendingRoom.Id, "server", a.Id, "old-token").Success && a.CurrentRoomId == pendingRoom.Id, "stale disconnect cannot remove current connection");
Check(reservationRooms.ClientDisconnected(pendingRoom.Id, "server", a.Id, responseA.AdmissionToken).Success && reservationRooms.FindById(pendingRoom.Id) != null, "keep room while another reservation exists");
clock.Advance(TimeSpan.FromSeconds(31));
reservationRooms.ExpireReservations();
Check(b.PendingRoomId == "" && pendingRoom.ReservedUsers == 0, "expire reservation without client notification");
Check(!reservationRooms.ConfirmConnection(pendingRoom.Id, "server", b.Id, responseB.AdmissionToken).Success, "reject expired token");
Ready(pendingRoom, "new-server");
var retry = await Reserve(b, pendingRoom);
Check(retry.Success, "retry after expiry");
Check(reservationRooms.LeaveRoom(new LeaveRoomRequest { UserId = b.Id, LobbyId = waitingLobby.Id, RoomId = pendingRoom.Id }).Success && b.PendingRoomId == "", "cancel pending reservation via leave API");
Ready(pendingRoom, "last-server");
var finalReservation = await Reserve(a, pendingRoom);
Check(reservationRooms.ConfirmConnection(pendingRoom.Id, "last-server", a.Id, finalReservation.AdmissionToken).Success, "reconnect with fresh token");
reservationRooms.ServerDisconnected(pendingRoom.Id, "new-server");
Check(!reservationRooms.GetRoomMaster(pendingRoom.Id, "new-server").Success,
    "stale server connection cannot query current room authority");
Check(a.CurrentRoomId == pendingRoom.Id, "ignore stale server disconnect");
Check(reservationRooms.SetRoomEntryLock(a.Id, new SetRoomEntryLockRequest { LobbyId = waitingLobby.Id, RoomId = pendingRoom.Id, IsEntryLocked = true }).Success,
    "lock room before dedicated server reset");
reservationRooms.ServerDisconnected(pendingRoom.Id, "last-server");
Check(!pendingRoom.IsEntryLocked, "dedicated server reset clears lock so room can restart without master");
Check(a.CurrentRoomId == "" && pendingRoom.Users.IsEmpty, "clear membership when active server disconnects");
var json = System.Text.Json.JsonSerializer.Serialize(pendingRoom);
Check(!json.Contains("test-secret") && !json.Contains("ServerToken"), "server secret excluded from JSON");
var failed = await Reserve(a, pendingRoom); // No Unity executable exists in the test build.
Check(!failed.Success && a.PendingRoomId == "" && pendingRoom.ReservedUsers == 0, "release reservation after server launch failure");

ConfigurationChecks.Run();
await CatalogLobbyChecks.Run();
await OperationsChecks.Run();
await OperationsHistoryChecks.Run();
await MaintenanceChecks.Run();
await ShutdownChecks.Run();
await OperatingModeChecks.Run();
await SettingsChecks.Run();
await ConcurrentRoomChecks.Run();
await JoinCancellationChecks.Run();
await LobbyUpdatesChecks.Run();
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
await ApiContractChecks.Run();

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}


