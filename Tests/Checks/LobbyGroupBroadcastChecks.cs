using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class LobbyGroupBroadcastChecks
{
    public static async Task Run()
    {
        var clock = new GroupClock();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }), clock);
        var lobbies = TestCatalogLobbies.Create(users, "group-broadcast");
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions()));
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        var lobby = TestCatalogLobbies.Get(lobbies, "group-broadcast");
        foreach (var connection in new[] { "one", "two", "catalog" })
        {
            var login = users.Login(new LoginRequest { UserName = connection });
            if (connection != "catalog")
            {
                await lobbies.JoinLobby(login.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            }

            updates.Subscribe(connection, connection == "catalog" ? "" : lobby.Id, "Bearer " + login.AccessToken);
        }

        var context = new RecordingContext();
        using var worker = new LobbyRoomUpdatesService(updates, context, NullLogger<LobbyRoomUpdatesService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (context.Sends.Count < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var sends = context.Sends.ToArray();
        if (sends.Length != 2 || sends.Any(send => send.Method != "RoomsChanged") ||
            sends.Count(send => send.Group == LobbyRoomUpdates.GetGroupName(lobby.Id)) != 1 ||
            sends.Count(send => send.Group == LobbyRoomUpdates.GetGroupName("")) != 1)
        {
            throw new Exception("shared lobby notifications must broadcast once per group");
        }

        Console.WriteLine("PASS: common lobby notifications broadcast once per group");
        if (!context.Manager.Members(LobbyRoomUpdates.GetGroupName(lobby.Id)).SequenceEqual(new[] { "one", "two" }))
        {
            throw new Exception("notification groups must contain the correct connections");
        }

        Console.WriteLine("PASS: notification worker synchronizes group membership before broadcasting");
        clock.Advance(TimeSpan.FromMinutes(61));
        await context.Manager.Synchronize(updates);
        if (context.Manager.Members(LobbyRoomUpdates.GetGroupName(lobby.Id)).Length != 0 ||
            context.Manager.Members(LobbyRoomUpdates.GetGroupName("")).Length != 0 ||
            updates.PrepareChat("one", "expired").Success)
        {
            throw new Exception("expired sessions must leave all lobby groups");
        }

        Console.WriteLine("PASS: expired sessions leave lobby and catalog groups before sending");
    }

    private sealed class GroupClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }

        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
        }
    }

    private sealed class RecordingContext : IHubContext<LobbyUpdatesHub>
    {
        public ConcurrentQueue<(string Group, string Method)> Sends { get; } = new();
        public TestSignalRGroups Manager { get; } = new();
        public IGroupManager Groups { get { return Manager; } }
        public IHubClients Clients { get; }

        public RecordingContext()
        {
            Clients = new RecordingClients(Sends);
        }
    }

    private sealed class RecordingClients : IHubClients
    {
        private readonly ConcurrentQueue<(string Group, string Method)> sends;

        public RecordingClients(ConcurrentQueue<(string Group, string Method)> sends)
        {
            this.sends = sends;
        }

        public IClientProxy Group(string groupName)
        {
            return new RecordingProxy(groupName, sends);
        }

        public IClientProxy All { get { throw new NotSupportedException(); } }
        public IClientProxy AllExcept(IReadOnlyList<string> ids) { throw new NotSupportedException(); }
        public IClientProxy Client(string id) { throw new NotSupportedException(); }
        public IClientProxy Clients(IReadOnlyList<string> ids) { throw new NotSupportedException(); }
        public IClientProxy GroupExcept(string group, IReadOnlyList<string> ids) { throw new NotSupportedException(); }
        public IClientProxy Groups(IReadOnlyList<string> names) { throw new NotSupportedException(); }
        public IClientProxy User(string id) { throw new NotSupportedException(); }
        public IClientProxy Users(IReadOnlyList<string> ids) { throw new NotSupportedException(); }
    }

    private sealed class RecordingProxy : IClientProxy
    {
        private readonly string group;
        private readonly ConcurrentQueue<(string Group, string Method)> sends;

        public RecordingProxy(string group, ConcurrentQueue<(string Group, string Method)> sends)
        {
            this.group = group;
            this.sends = sends;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            sends.Enqueue((group, method));
            return Task.CompletedTask;
        }
    }
}
