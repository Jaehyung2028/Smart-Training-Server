using Microsoft.Extensions.Options;
using Smart_Training_Server.Enum;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

internal static class JoinCancellationChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
        var processes = new Processes();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { NameMinLength = 1, NameMaxLength = 20, MaxCount = 10 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 8, ServerReadyTimeoutSeconds = 60 }), processManager: processes);
        var lobby = lobbies.CreateLobby(new CreateLobbyRequest { LobbyName = "cancel" }).Lobby!;
        lobby.Contents["test"] = new ContentInfo { RootContent = "test" };
        async Task<UserInfo> Add(string name)
        {
            var user = users.Login(new LoginRequest { UserName = name }).User!;
            await lobbies.JoinLobby(new JoinLobbyRequest { UserId = user.Id, LobbyId = lobby.Id }); return user;
        }
        var a = await Add("a"); var b = await Add("b");
        var room = rooms.CreateRoom(new CreateRoomRequest { UserId = a.Id, LobbyId = lobby.Id, ContentId = "test", RoomName = "cancel-room", MaxUsers = 2 }).Room!;
        JoinRoomRequest Request(UserInfo user) => new() { UserId = user.Id, LobbyId = lobby.Id, RoomId = room.Id };

        using var before = new CancellationTokenSource(); before.Cancel();
        Check(!(await rooms.JoinRoom(Request(a), before.Token)).Success && room.ReservedUsers == 0 && a.PendingRoomId == "" && processes.Launches == 0,
            "cancel before reservation creates no reservation or server process");

        using var single = new CancellationTokenSource();
        var waiting = rooms.JoinRoom(Request(a), single.Token); string oldSecret = room.ServerToken;
        Check(room.State == ServerState.Starting && room.ReservedUsers == 1, "cancellation fixture waits for actual readiness task");
        single.Cancel();
        Check(!(await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Success && room.ReservedUsers == 0 && a.PendingRoomId == "" &&
            room.State == ServerState.Creating && !processes.IsRunning(room.Id), "cancel during readiness promptly releases reservation and unused launch");
        Check(!rooms.RegisterServer(room.Id, oldSecret, "late"), "cancelled launch cannot register late");

        using var shared = new CancellationTokenSource();
        var first = rooms.JoinRoom(Request(a), shared.Token);
        var second = rooms.JoinRoom(Request(b)); string sharedSecret = room.ServerToken;
        shared.Cancel();
        Check(!(await first.WaitAsync(TimeSpan.FromSeconds(2))).Success && room.ReservedUsers == 1 && a.PendingRoomId == "" && b.PendingRoomId == room.Id && processes.IsRunning(room.Id),
            "cancel one waiter preserves another participant reservation and shared launch");
        Check(rooms.RegisterServer(room.Id, sharedSecret, "shared"), "shared launch still registers after other waiter cancellation");
        var admissionB = await second.WaitAsync(TimeSpan.FromSeconds(2));
        Check(admissionB.Success && rooms.ConfirmConnection(room.Id, "shared", b.Id, admissionB.AdmissionToken).Success,
            "uncancelled waiter confirms normally");
        var admissionA = await rooms.JoinRoom(Request(a));
        Check(admissionA.Success && rooms.ConfirmConnection(room.Id, "shared", a.Id, admissionA.AdmissionToken).Success,
            "cancelled user can retry without duplicate reservation");

        // A cancel arriving after the HTTP response must use normal leave cleanup.
        Check(rooms.LeaveRoom(new LeaveRoomRequest { UserId = a.Id, LobbyId = lobby.Id, RoomId = room.Id }).Success &&
            a.CurrentRoomId == "" && room.Users.Count == 1 && room.Users.ContainsKey(b.Id), "late cancellation leaves confirmed user without affecting remaining participant");
        Check(!rooms.ClientDisconnected(room.Id, "shared", a.Id, admissionA.AdmissionToken).Success && room.Users.ContainsKey(b.Id),
            "late old transport callback cannot remove remaining participant");

        var httpLogin = users.Login(new LoginRequest { UserName = "http-cancel" });
        var httpUser = httpLogin.User!;
        await lobbies.JoinLobby(new JoinLobbyRequest { UserId = httpUser.Id, LobbyId = lobby.Id });
        var httpRoom = rooms.CreateRoom(new CreateRoomRequest { UserId = httpUser.Id, LobbyId = lobby.Id, ContentId = "test", RoomName = "http-cancel", MaxUsers = 2 }).Room!;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(users).AddSingleton(lobbies).AddSingleton(rooms);
        builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(UserTokenAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(Smart_Training_Server._Controllers.RoomController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", httpLogin.AccessToken);
            using var httpCancel = new CancellationTokenSource();
            var pendingHttp = client.PostAsJsonAsync("api/Room/join", new { lobbyId = lobby.Id, roomId = httpRoom.Id }, httpCancel.Token);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (httpRoom.ReservedUsers == 0 && !pendingHttp.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(10);
            Check(httpRoom.ReservedUsers == 1 && httpRoom.State == ServerState.Starting, "real HTTP join reaches pending server readiness");
            string httpSecret = httpRoom.ServerToken;
            httpCancel.Cancel();
            try { using var unexpected = await pendingHttp; throw new InvalidOperationException("HTTP cancellation was not observed"); }
            catch (OperationCanceledException) { }
            deadline = DateTime.UtcNow.AddSeconds(3);
            while (httpRoom.ReservedUsers != 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Check(httpRoom.ReservedUsers == 0 && httpUser.PendingRoomId == "" && httpRoom.State == ServerState.Creating && !processes.IsRunning(httpRoom.Id),
                "HTTP request abort reaches controller and cleans pending admission promptly");
            Check(!rooms.RegisterServer(httpRoom.Id, httpSecret, "late-http"), "HTTP-aborted launch rejects late server registration");
        }
        finally { await app.StopAsync(); }
    }
    private sealed class Processes : IRoomServerProcessManager
    {
        private readonly HashSet<string> active = new();
        public int Launches { get; private set; }
        public void Launch(string id, int port, string path, string token) { active.Add(id); Launches++; }
        public bool IsRunning(string id) => active.Contains(id);
        public void Stop(string id) => active.Remove(id);
    }
}
