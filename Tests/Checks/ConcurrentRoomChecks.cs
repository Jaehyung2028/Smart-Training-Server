using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class ConcurrentRoomChecks
{
    private const int Repetitions = 40;
    private static void Require(bool value, string name)
    { if (!value) throw new InvalidOperationException("Concurrent room check: " + name); }

    // Release all asynchronous workers from the same gate. No sleeps or fake sequential races.
    private static async Task Race(params Func<Task>[] actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = actions.Select(action => Task.Run(async () => { await gate.Task; await action(); })).ToArray();
        gate.SetResult();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class Fixture
    {
        public readonly UserService Users = new(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        public readonly LobbyService Lobbies;
        public readonly RoomService Rooms;
        public readonly LobbyInfo Lobby;
        public RoomInfo Room = null!;
        public const string Server = "race-server";
        public Fixture()
        {
            Lobbies = new(new ContentService(), Users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
            Rooms = new(Lobbies, Users, Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 8 }));
            Lobby = Lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "race" }).Lobby!;
            Lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
        }
        public async Task<UserInfo> Add(string name)
        {
            var user = Users.Login(new LoginRequest { UserName = name }).User!;
            Require((await Lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = Lobby.Id })).Success, "fixture lobby entry");
            return user;
        }
        public void Create(UserInfo owner, int capacity)
        {
            Room = Rooms.CreateRoom(new CreateRoomRequest { UserId = owner.Id, LobbyId = Lobby.Id, RoomName = "race-room", ContentId = "test", MaxUsers = capacity }).Room!;
            Room.State = ServerState.Starting; Room.ServerToken = "fixture-secret";
            Require(Rooms.RegisterServer(Room.Id, Room.ServerToken, Server), "fixture registered server");
        }
        public Task<JoinRoomResponse> Reserve(UserInfo user) => Rooms.JoinRoom(new JoinRoomRequest { UserId = user.Id, LobbyId = Lobby.Id, RoomId = Room.Id });
        public bool Confirm(UserInfo user, string token) => Rooms.ConfirmConnection(Room.Id, Server, user.Id, token).Success;
        public bool Disconnect(UserInfo user, string token) => Rooms.ClientDisconnected(Room.Id, Server, user.Id, token).Success;
        public async Task<string> Connect(UserInfo user)
        { var admission = await Reserve(user); Require(admission.Success && Confirm(user, admission.AdmissionToken), "fixture confirmed user"); return admission.AdmissionToken; }
        public void Consistent()
        {
            Require(Room.Users.Count + Room.ReservedUsers <= Room.MaxUsers, "connected plus reserved capacity");
            Require(Room.ReservedUsers == Lobby.Users.Values.Count(user => user.PendingRoomId == Room.Id), "reservation count matches user state");
            Require(Room.Users.Values.All(user => user.CurrentRoomId == Room.Id && user.PendingRoomId == ""), "membership identity matches room");
            Require(Room.Users.Values.Select(user => user.RoomIndex).Distinct().Count() == Room.Users.Count, "unique participant indexes");
            Require(Room.Users.IsEmpty || Room.Users.ContainsKey(Room.MasterUserId), "master belongs to current participants");
        }
    }

    public static async Task Run()
    {
        for (int attempt = 0; attempt < Repetitions; attempt++)
        {
            // Twelve contenders compete for the only free slot beside an established owner.
            var capacity = new Fixture(); var owner = await capacity.Add("owner"); capacity.Create(owner, 2); await capacity.Connect(owner);
            var contenders = new List<UserInfo>();
            for (int n = 0; n < 12; n++) contenders.Add(await capacity.Add("contender" + n));
            var results = new JoinRoomResponse[contenders.Count];
            await Race(contenders.Select((person, index) => (Func<Task>)(async () => results[index] = await capacity.Reserve(person))).ToArray());
            Require(results.Count(result => result.Success) == 1 && capacity.Room.ReservedUsers == 1, "only one last-slot reservation succeeds");
            int winner = Array.FindIndex(results, result => result.Success);
            Require(capacity.Confirm(contenders[winner], results[winner].AdmissionToken) && capacity.Room.Users.Count == 2, "winner confirms without exceeding capacity");
            capacity.Consistent();

            // The master's departure overlaps the newcomer's actual connection confirmation.
            var authority = new Fixture(); owner = await authority.Add("owner"); var member = await authority.Add("member"); var arrival = await authority.Add("arrival");
            authority.Create(owner, 3); string ownerToken = await authority.Connect(owner); await authority.Connect(member);
            var arriving = await authority.Reserve(arrival); bool departed = false, confirmed = false;
            await Race(() => { departed = authority.Disconnect(owner, ownerToken); return Task.CompletedTask; },
                () => { confirmed = authority.Confirm(arrival, arriving.AdmissionToken); return Task.CompletedTask; });
            Require(departed && confirmed && owner.CurrentRoomId == "" && authority.Room.Users.Count == 2 && authority.Room.MasterUserId != owner.Id,
                "master departure and arrival preserve valid authority");
            authority.Consistent();

            // Starting excludes an in-flight reservation, regardless of which operation wins.
            var phase = new Fixture(); owner = await phase.Add("owner"); arrival = await phase.Add("arrival"); phase.Create(owner, 2);
            ownerToken = await phase.Connect(owner); var roster = System.Text.Json.JsonSerializer.Serialize(new[] { owner.Id });
            RoomMasterResponse? started = null; JoinRoomResponse? reserved = null;
            await Race(() => { started = phase.Rooms.SetRoomPhase(phase.Room.Id, Fixture.Server, owner.Id, ownerToken, true, roster, 0); return Task.CompletedTask; },
                async () => reserved = await phase.Reserve(arrival));
            Require(started!.Success != reserved!.Success, "start and new reservation cannot both succeed");
            Require(phase.Room.IsRunning == started.Success && phase.Room.PhaseRevision == (started.Success ? 1 : 0), "phase revision matches winning transition");
            if (reserved.Success) Require(phase.Confirm(arrival, reserved.AdmissionToken), "reservation winner can finish admission");
            phase.Consistent();

            // A delayed old disconnect overlaps confirmation of a fresh reconnect token.
            var reconnect = new Fixture(); owner = await reconnect.Add("owner"); member = await reconnect.Add("member"); reconnect.Create(owner, 2);
            await reconnect.Connect(owner); string oldToken = await reconnect.Connect(member);
            Require(reconnect.Disconnect(member, oldToken), "initial disconnect");
            var fresh = await reconnect.Reserve(member); bool staleAccepted = false; confirmed = false;
            await Race(() => { staleAccepted = reconnect.Disconnect(member, oldToken); return Task.CompletedTask; },
                () => { confirmed = reconnect.Confirm(member, fresh.AdmissionToken); return Task.CompletedTask; });
            Require(!staleAccepted && confirmed && reconnect.Room.Users.Count == 2 && member.CurrentRoomId == reconnect.Room.Id,
                "stale disconnect cannot remove reconnecting membership");
            Require(reconnect.Confirm(member, fresh.AdmissionToken) && reconnect.Room.Users.Count == 2 && reconnect.Room.ReservedUsers == 0,
                "duplicate reconnect confirmation remains idempotent");
            reconnect.Consistent();
        }
        foreach (var scenario in new[] { "last free slot with twelve concurrent contenders", "master departure overlapping admission confirmation",
            "room start overlapping new admission reservation", "fresh reconnect overlapping stale disconnect" })
            Console.WriteLine($"PASS: {scenario} ({Repetitions} repetitions with capacity, membership and authority invariants)");
    }
}
