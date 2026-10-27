using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class LogoutChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.Create(users, "logout-lobby", "departed-lobby", "startup-lobby");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 10
                })
        );
        var hub = new RecordingRoomHub();
        var lifecycle = new UserLifecycleService(users, rooms, lobbies, hub, NullLogger<UserLifecycleService>.Instance);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(users).AddSingleton(lobbies).AddSingleton(rooms).AddSingleton(lifecycle);
        builder.Services.AddSingleton<IHubContext<RoomHub>>(hub);
        builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(UserTokenAuthenticationHandler.SchemeName, _ =>
        {
        });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient
            {
                BaseAddress = new Uri(address)
            };
            Check(
                (await client.PostAsync("api/Auth/logout", null)).StatusCode == HttpStatusCode.Unauthorized,
                "logout requires authentication"
            );
            var first = users.Login(new LoginRequest { UserName = "logout-first" });
            var second = users.Login(new LoginRequest { UserName = "logout-second" });
            var lobby = TestCatalogLobbies.Get(lobbies, "logout-lobby");
            await lobbies.JoinLobby(first.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            await lobbies.JoinLobby(second.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            RoomInfo CreateRoom(string name) => TestRoomSetup.Create(rooms,
                first.User.Id,
                new CreateRoomRequest {
                    LobbyId = lobby.Id,
                    RoomName = name,
                    ContentId = "sample-training", ContentNodeId = 2,
                    MaxUsers = 3
                }
            ).Room!;
            void Ready(RoomInfo room, string connection)
            {
                room.State = Smart_Training_Server.Enum.ServerState.Starting;
                room.ServerToken = "fixture-secret";
                Check(rooms.RegisterServer(room.Id, room.ServerToken, connection), "prepare logout room server");
            }

            var room = CreateRoom("shared-room");
            var emptyOwned = CreateRoom("owned-empty");
            Ready(room, "shared-server");
            var firstAdmission = await rooms.JoinRoom(first.User.Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = room.Id
                });
            var secondAdmission = await rooms.JoinRoom(second.User.Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = room.Id
                });
            rooms.ConfirmConnection(room.Id, "shared-server", first.User.Id, firstAdmission.AdmissionToken);
            rooms.ConfirmConnection(room.Id, "shared-server", second.User.Id, secondAdmission.AdmissionToken);
            var lockBody = new
            {
                lobbyId = lobby.Id,
                roomId = room.Id,
                isEntryLocked = true,
                userId = first.User.Id
            };
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", lockBody)).StatusCode == HttpStatusCode.Unauthorized,
                "entry lock requires authenticated token"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", lockBody)).StatusCode == HttpStatusCode.BadRequest && !room.IsEntryLocked,
                "ordinary participant cannot impersonate master to lock room"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", new { lobbyId = lobby.Id, roomId = room.Id })).StatusCode == HttpStatusCode.BadRequest && !room.IsEntryLocked,
                "omitted lock flag cannot accidentally change room access"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", new { lobbyId = "wrong-lobby", roomId = room.Id, isEntryLocked = true })).StatusCode == HttpStatusCode.BadRequest && !room.IsEntryLocked,
                "entry lock rejects mismatched lobby"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", lockBody)).StatusCode == HttpStatusCode.OK && room.IsEntryLocked && room.Users.Count == 2 && first.User.CurrentRoomId == room.Id && second.User.CurrentRoomId == room.Id,
                "master locks entry while preserving existing memberships"
            );
            Check(
                rooms.GetRoomByLobby(lobby.Id, room.Id)?.IsEntryLocked == true && rooms.GetRoomMaster(room.Id, "shared-server").IsEntryLocked,
                "room query and dedicated-server query agree on entry lock"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = second.User.Id })).StatusCode == HttpStatusCode.OK && room.IsEntryLocked,
                "manual master transfer preserves entry lock"
            );
            var unlockBody = new
            {
                lobbyId = lobby.Id,
                roomId = room.Id,
                isEntryLocked = false
            };
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", unlockBody)).StatusCode == HttpStatusCode.BadRequest && room.IsEntryLocked,
                "former master immediately loses permission to unlock"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", unlockBody)).StatusCode == HttpStatusCode.OK && !room.IsEntryLocked,
                "new master can reopen entry"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/entry-lock", unlockBody)).StatusCode == HttpStatusCode.OK && room.Users.Count == 2,
                "repeated unlock leaves participant count unchanged"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = first.User.Id })).StatusCode == HttpStatusCode.OK,
                "restore initial master after access checks"
            );
            client.DefaultRequestHeaders.Authorization = null;
            var kickBody = new
            {
                lobbyId = lobby.Id,
                roomId = room.Id,
                targetUserId = second.User.Id,
                userId = first.User.Id
            };
            var settingsBody = new
            {
                lobbyId = lobby.Id,
                roomId = room.Id,
                roomName = "settings-http",
                maxUsers = room.MaxUsers,
                userId = first.User.Id
            };
            Check(
                (await client.PostAsJsonAsync("api/Room/settings", settingsBody)).StatusCode == HttpStatusCode.Unauthorized,
                "settings endpoint requires authentication"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/settings", settingsBody)).StatusCode == HttpStatusCode.BadRequest,
                "settings endpoint ignores impersonated body user ID"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/settings", new { lobbyId = lobby.Id, roomId = room.Id, roomName = "missing-capacity" })).StatusCode == HttpStatusCode.BadRequest,
                "settings endpoint rejects omitted capacity"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/settings", settingsBody)).StatusCode == HttpStatusCode.OK && second.User.CurrentRoomName == "settings-http",
                "settings endpoint updates authenticated master room and participant names"
            );
            client.DefaultRequestHeaders.Authorization = null;
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", kickBody)).StatusCode == HttpStatusCode.Unauthorized,
                "kick requires authenticated token"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync(
                    "api/Room/kick",
                    new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = first.User.Id, userId = first.User.Id }
                )).StatusCode == HttpStatusCode.BadRequest,
                "ordinary participant cannot impersonate master to kick"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = first.User.Id })).StatusCode == HttpStatusCode.BadRequest,
                "master cannot kick self"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", new { lobbyId = "wrong-lobby", roomId = room.Id, targetUserId = second.User.Id })).StatusCode == HttpStatusCode.BadRequest,
                "kick rejects mismatched lobby"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = "missing-user" })).StatusCode == HttpStatusCode.BadRequest,
                "kick rejects missing participant"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", new { lobbyId = lobby.Id, roomId = (string? )null, targetUserId = second.User.Id })).StatusCode == HttpStatusCode.BadRequest,
                "kick rejects null identifiers without exception"
            );
            Check(
                !hub.Messages.Any(message => message.Method == "KickUser"),
                "rejected kicks send no dedicated-server command"
            );
            hub.FailSend = true;
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", kickBody)).StatusCode == HttpStatusCode.BadRequest && room.Users.Count == 2 && second.User.CurrentRoomId == room.Id,
                "failed kick delivery preserves actual connected membership"
            );
            hub.FailSend = false;
            Check(
                (await client.PostAsJsonAsync("api/Room/kick", kickBody)).StatusCode == HttpStatusCode.OK && room.Users.Count == 2,
                "kick request keeps slot occupied until server disconnect confirmation"
            );
            var oldSecondToken = secondAdmission.AdmissionToken;
            Check(
                hub.Messages.Any(message => message.Connection == "shared-server" && message.Method == "KickUser" && (string)message.Arguments[0]! == second.User.Id && (string)message.Arguments[1]! == oldSecondToken),
                "kick targets exact dedicated connection and admission token"
            );
            Check(
                rooms.ClientDisconnected(room.Id, "shared-server", second.User.Id, oldSecondToken).Success && room.Users.Count == 1 && second.User.CurrentRoomId == "" && second.User.CurrentLobbyId == lobby.Id && users.FindByAccessToken(second.AccessToken) != null,
                "confirmed kick frees slot and preserves login and lobby"
            );
            secondAdmission = await rooms.JoinRoom(second.User.Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = room.Id
                });
            Check(
                secondAdmission.Success && rooms.ConfirmConnection(room.Id, "shared-server", second.User.Id, secondAdmission.AdmissionToken).Success,
                "kicked participant can rejoin with fresh admission"
            );
            Check(
                !rooms.ClientDisconnected(room.Id, "shared-server", second.User.Id, oldSecondToken).Success && second.User.CurrentRoomId == room.Id,
                "stale kick disconnect cannot remove rejoined participant"
            );
            client.DefaultRequestHeaders.Authorization = null;
            var transferBody = new
            {
                lobbyId = lobby.Id,
                roomId = room.Id,
                targetUserId = first.User.Id,
                userId = first.User.Id
            };
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", transferBody)).StatusCode == HttpStatusCode.Unauthorized,
                "manual master transfer requires token authentication"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", transferBody)).StatusCode == HttpStatusCode.BadRequest && room.MasterUserId == first.User.Id,
                "non-master cannot impersonate master through request body"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            Check(
                (await client.PostAsJsonAsync(
                    "api/Room/transfer-master",
                    new { lobbyId = "wrong-lobby", roomId = room.Id, targetUserId = second.User.Id }
                )).StatusCode == HttpStatusCode.BadRequest && room.MasterUserId == first.User.Id,
                "master transfer rejects mismatched lobby"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = "missing-user" })).StatusCode == HttpStatusCode.BadRequest && room.MasterUserId == first.User.Id,
                "master transfer rejects missing target without changing authority"
            );
            Check(
                (await client.PostAsJsonAsync(
                    "api/Room/transfer-master",
                    new { lobbyId = lobby.Id, roomId = (string? )null, targetUserId = second.User.Id }
                )).StatusCode == HttpStatusCode.BadRequest,
                "master transfer rejects null identifiers without server exception"
            );
            var transfer = await client.PostAsJsonAsync("api/Room/transfer-master", new { lobbyId = lobby.Id, roomId = room.Id, targetUserId = second.User.Id });
            var transferResult = await transfer.Content.ReadFromJsonAsync<Smart_Training_Server.Responses.Room.RoomMasterResponse>();
            Check(
                transfer.StatusCode == HttpStatusCode.OK && transferResult?.MasterUserId == second.User.Id && room.MasterUserId == second.User.Id,
                "authenticated master transfers role to connected participant"
            );
            Check(
                room.Users.Count == 2 && first.User.CurrentRoomId == room.Id && second.User.CurrentRoomId == room.Id,
                "manual transfer preserves both memberships"
            );
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", transferBody)).StatusCode == HttpStatusCode.BadRequest && room.MasterUserId == second.User.Id,
                "former master loses transfer permission immediately"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsJsonAsync("api/Room/transfer-master", transferBody)).StatusCode == HttpStatusCode.OK && room.MasterUserId == first.User.Id,
                "new master can transfer authority back"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var logout = await client.PostAsJsonAsync("api/Auth/logout", new { userId = second.User.Id });
            Check(
                logout.StatusCode == HttpStatusCode.OK && users.FindById(first.User.Id) == null && users.FindByAccessToken(first.AccessToken) == null,
                "HTTP logout revokes token and removes authenticated user"
            );
            Check(
                users.FindByAccessToken(second.AccessToken) != null && room.Users.ContainsKey(second.User.Id),
                "logout body cannot target another user"
            );
            Check(
                room.MasterUserId == second.User.Id && room.Users.Count == 1,
                "logout transfers master and keeps shared room"
            );
            Check(
                rooms.FindById(emptyOwned.Id) == null && !lobby.Rooms.ContainsKey(emptyOwned.Id),
                "logout removes owned empty room"
            );
            Check(
                !lobby.Users.ContainsKey(first.User.Id) && lobbies.FindById(lobby.Id) != null,
                "logout removes lobby membership and preserves other users"
            );
            Check(
                first.User.CurrentRoomId == "" && first.User.PendingRoomId == "" && first.User.CurrentLobbyId == "" && first.User.RoomIndex == -1 && first.User.LobbyIndex == -1,
                "logout clears user state"
            );
            Check(
                hub.Messages.Any(x => x.Connection == "shared-server" && x.Method == "DisconnectUser" && (string)x.Arguments[0]! == first.User.Id && (string)x.Arguments[1]! == firstAdmission.AdmissionToken),
                "send Unity disconnect request for exact old connection"
            );
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized && (await client.PostAsync("api/Auth/logout", null)).StatusCode == HttpStatusCode.Unauthorized,
                "revoked token cannot query or logout again"
            );
            var fresh = users.Login(new LoginRequest { UserName = "logout-first" });
            Check(
                fresh.Success && fresh.User!.Id != first.User.Id && fresh.AccessToken != first.AccessToken,
                "same nickname can log in with fresh identity"
            );
            Check(
                !rooms.ClientDisconnected(room.Id, "shared-server", first.User.Id, firstAdmission.AdmissionToken).Success && room.Users.ContainsKey(second.User.Id),
                "late old disconnect cannot remove remaining user"
            );
            await lifecycle.LogoutAsync(first.User.Id);
            Check(users.FindByAccessToken(fresh.AccessToken) != null, "repeat cleanup cannot remove relogged nickname");
            await lobbies.JoinLobby(fresh.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            var pending = await rooms.JoinRoom(fresh.User.Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = room.Id
                });
            Check(pending.Success && room.ReservedUsers == 1, "prepare pending logout reservation");
            Check(
                rooms.SetRoomEntryLock(
                    second.User.Id,
                    new SetRoomEntryLockRequest {
                        LobbyId = lobby.Id,
                        RoomId = room.Id,
                        IsEntryLocked = true
                    }
                ).Success && fresh.User.PendingRoomId == "" && room.ReservedUsers == 0,
                "locking cancels pending reservations and frees reserved slots"
            );
            Check(
                !rooms.ValidateConnection(room.Id, "shared-server", fresh.User.Id, pending.AdmissionToken).Success && !rooms.ConfirmConnection(room.Id, "shared-server", fresh.User.Id, pending.AdmissionToken).Success,
                "locked room rejects approval and confirmation with previously issued token"
            );
            Check(
                rooms.ConfirmConnection(room.Id, "shared-server", second.User.Id, secondAdmission.AdmissionToken).Success,
                "entry lock preserves idempotent confirmation of existing participant"
            );
            Check(
                !(await rooms.JoinRoom(fresh.User.Id, new JoinRoomRequest {
                        LobbyId = lobby.Id,
                        RoomId = room.Id
                    })).Success && fresh.User.PendingRoomId == "" && room.ReservedUsers == 0,
                "locked room creates no new reservation"
            );
            Check(
                rooms.SetRoomEntryLock(
                    second.User.Id,
                    new SetRoomEntryLockRequest {
                        LobbyId = lobby.Id,
                        RoomId = room.Id,
                        IsEntryLocked = false
                    }
                ).Success,
                "master unlocks after canceling reservation"
            );
            pending = await rooms.JoinRoom(fresh.User.Id, new JoinRoomRequest {
                    LobbyId = lobby.Id,
                    RoomId = room.Id
                });
            Check(
                pending.Success && room.ReservedUsers == 1 && pending.AdmissionToken != "",
                "reopened room issues fresh admission"
            );
            Check(
                !(await rooms.KickRoomUserAsync(
                    second.User.Id,
                    new RoomParticipantRequest {
                        LobbyId = lobby.Id,
                        RoomId = room.Id,
                        TargetUserId = fresh.User.Id
                    },
                    hub
                )).Success,
                "reservation without confirmed connection cannot be kicked"
            );
            Check(
                !rooms.TransferRoomMaster(
                    second.User.Id,
                    new RoomParticipantRequest {
                        LobbyId = lobby.Id,
                        RoomId = room.Id,
                        TargetUserId = fresh.User.Id
                    }
                ).Success && room.MasterUserId == second.User.Id,
                "reservation alone cannot receive master authority"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
            Check(
                (await client.PostAsync("api/Auth/logout", null)).StatusCode == HttpStatusCode.OK && room.ReservedUsers == 0 && room.Users.Count == 1,
                "logout cancels reservation without removing others"
            );
            Check(
                !rooms.ConfirmConnection(room.Id, "shared-server", fresh.User.Id, pending.AdmissionToken).Success,
                "logged-out admission cannot be confirmed"
            );
            hub.FailSend = true;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            Check(
                (await client.PostAsync("api/Auth/logout", null)).StatusCode == HttpStatusCode.OK && users.FindByAccessToken(second.AccessToken) == null,
                "notification failure does not restore login"
            );
            Check(
                rooms.FindById(room.Id) == null && lobbies.FindById(lobby.Id) != null,
                "last logout removes empty room but retains catalog lobby"
            );
            var idle = users.Login(new LoginRequest { UserName = "idle-logout" });
            Check(
                (await lifecycle.LogoutAsync(idle.User!.Id)).Success && users.FindById(idle.User.Id) == null,
                "logout user outside lobby"
            );
            var departed = users.Login(new LoginRequest { UserName = "departed-owner" });
            var departedLobby = TestCatalogLobbies.Get(lobbies, "departed-lobby");
            await lobbies.JoinLobby(departed.User!.Id, new LobbyMembershipRequest { LobbyId = departedLobby.Id });
            var owned = TestRoomSetup.Create(rooms,
                departed.User.Id,
                new CreateRoomRequest {
                    LobbyId = departedLobby.Id,
                    RoomName = "departed-room",
                    ContentId = "sample-training", ContentNodeId = 2,
                    MaxUsers = 2
                }
            ).Room!;
            lobbies.LeaveLobby(departed.User.Id, new LobbyMembershipRequest { LobbyId = departedLobby.Id });
            Check(lobbies.FindById(departedLobby.Id) != null, "lobby exit preserves owned empty room");
            await lifecycle.LogoutAsync(departed.User.Id);
            Check(
                lobbies.FindById(departedLobby.Id) != null && rooms.FindById(owned.Id) == null,
                "logout cleans owned room but retains catalog lobby after prior exit"
            );
            var startup = users.Login(new LoginRequest { UserName = "startup-owner" });
            var startupLobby = TestCatalogLobbies.Get(lobbies, "startup-lobby");
            await lobbies.JoinLobby(startup.User!.Id, new LobbyMembershipRequest { LobbyId = startupLobby.Id });
            var startingRoom = TestRoomSetup.Create(rooms,
                startup.User.Id,
                new CreateRoomRequest {
                    LobbyId = startupLobby.Id,
                    RoomName = "starting",
                    ContentId = "sample-training", ContentNodeId = 2,
                    MaxUsers = 2
                }
            ).Room!;
            startingRoom.State = Smart_Training_Server.Enum.ServerState.Starting;
            startingRoom.ServerToken = "old-start-secret";
            var waitingJoin = rooms.JoinRoom(startup.User.Id, new JoinRoomRequest {
                    LobbyId = startupLobby.Id,
                    RoomId = startingRoom.Id
                });
            await lifecycle.LogoutAsync(startup.User.Id);
            Check(
                !(await waitingJoin.WaitAsync(TimeSpan.FromSeconds(2))).Success && rooms.FindById(startingRoom.Id) == null,
                "logout unblocks in-flight server readiness wait"
            );
            Check(
                !rooms.RegisterServer(startingRoom.Id, "old-start-secret", "late-server"),
                "late server cannot register deleted room"
            );
        }
        finally
        {
            await app.StopAsync();
        }
    }
}

internal sealed class RecordingRoomHub : IHubContext<RoomHub>
{
    public sealed record Message(string Connection, string Method, object? [] Arguments);

    public List<Message> Messages { get; } = new();

    public bool FailSend { get; set; }

    public IHubClients Clients => new ClientsRecorder(this);

    public IGroupManager Groups => throw new NotSupportedException();

    private sealed class ClientsRecorder(RecordingRoomHub owner) : IHubClients
    {
        public IClientProxy Client(string connectionId) => new Proxy(owner, connectionId);

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> ids) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> ids) => throw new NotSupportedException();

        public IClientProxy Group(string name) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> names) => throw new NotSupportedException();

        public IClientProxy User(string id) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> ids) => throw new NotSupportedException();
    }

    private sealed class Proxy(RecordingRoomHub owner, string connection) : IClientProxy
    {
        public Task SendCoreAsync(string method, object? [] args, CancellationToken cancellationToken = default)
        {
            if (owner.FailSend)
            {
                throw new IOException("simulated send failure");
            }

            owner.Messages.Add(new(connection, method, args));
            return Task.CompletedTask;
        }
    }
}
