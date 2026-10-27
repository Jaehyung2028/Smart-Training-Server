using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Auth;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class AuthChecks
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

        // Public HTTP requests carry operation inputs only; caller identity comes from the token.
        foreach (var requestType in new[]
        {
            typeof(LobbyMembershipRequest),
            typeof(LobbyMembershipRequest),
            typeof(CreateRoomRequest),
            typeof(JoinRoomRequest),
            typeof(RoomMembershipRequest)
        }

        )
        {
            Check(requestType.GetProperty("UserId") == null, requestType.Name + " excludes caller ID from public contract");
            var json = JsonSerializer.Serialize(Activator.CreateInstance(requestType), requestType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(!json.Contains("\"userId\""), requestType.Name + " JSON contains no caller ID");
        }

        Check(
            typeof(RoomParticipantRequest).GetProperty("TargetUserId") != null && typeof(RoomParticipantRequest).GetProperty("TargetUserId") != null,
            "master transfer and kick still identify their target participant"
        );
        var clock = new TestClock();
        var users = new UserService(
            Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    AccessTokenLifetimeMinutes = 1
                }),
            clock
        );
        var lobbies = TestCatalogLobbies.Create(users, "auth-lobby", "grace-lobby");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 10
                }),
            clock
        );
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(users).AddSingleton(lobbies).AddSingleton(rooms);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<UserLifecycleService>();
        builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(UserTokenAuthenticationHandler.SchemeName, _ =>
        {
        });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(
                "Bearer",
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme {
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "bearer"
                }
            );
            options.OperationFilter<SwaggerUserAuthenticationFilter>();
        });
        await using var app = builder.Build();
        app.UseSwagger();
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
            var unauthorized = await client.GetAsync("api/Lobby");
            var health = await client.GetAsync("api/health");
            Check(
                health.StatusCode == HttpStatusCode.OK && health.Headers.CacheControl?.NoStore == true && (await health.Content.ReadAsStringAsync()).Contains("\"status\":\"ok\""),
                "anonymous health query returns minimal uncached liveness status"
            );
            Check(
                (await client.GetAsync("api/Auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "current user query requires authentication"
            );
            Check(
                unauthorized.StatusCode == HttpStatusCode.Unauthorized && unauthorized.Headers.WwwAuthenticate.Any(x => x.Scheme == "Bearer"),
                "anonymous request returns JSON 401 and Bearer challenge"
            );
            foreach (var endpoint in new[]
            {
                "api/Lobby/join",
                "api/Lobby/join",
                "api/Lobby/leave",
                "api/Room/create",
                "api/Room/join",
                "api/Room/leave"
            }

            )
            {
                Check(
                    (await client.PostAsJsonAsync(endpoint, new { })).StatusCode == HttpStatusCode.Unauthorized,
                    "authentication required for " + endpoint
                );
            }

            Check(
                (await client.GetAsync("api/Lobby/missing/rooms")).StatusCode == HttpStatusCode.Unauthorized,
                "room list requires authentication before lookup"
            );
            Check(
                (await client.GetAsync("api/Lobby/missing/rooms/missing")).StatusCode == HttpStatusCode.Unauthorized,
                "room detail requires authentication before lookup"
            );
            var loginHttp = await client.PostAsJsonAsync("api/Auth/login", new LoginRequest { UserName = "auth-first" });
            var first = (await loginHttp.Content.ReadFromJsonAsync<LoginResponse>())!;
            Check(loginHttp.Headers.CacheControl?.NoStore == true, "login token response is not cacheable");
            Check(
                loginHttp.StatusCode == HttpStatusCode.OK && first.Success && first.AccessToken.Length == 64 && first.TokenType == "Bearer" && first.ExpiresAtUtc == clock.GetUtcNow().UtcDateTime.AddMinutes(1),
                "anonymous login issues token with configured expiry"
            );
            var second = (await (await client.PostAsJsonAsync("api/Auth/login", new LoginRequest { UserName = "auth-second" })).Content.ReadFromJsonAsync<LoginResponse>())!;
            Check(
                first.AccessToken != second.AccessToken && users.FindByAccessToken(first.AccessToken)?.Id == first.User!.Id,
                "random tokens bound to separate users"
            );
            var failedLogin = await client.PostAsJsonAsync("api/Auth/login", new LoginRequest { UserName = "auth-first" });
            Check(
                failedLogin.StatusCode == HttpStatusCode.BadRequest && (await failedLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken == "",
                "failed login does not issue token"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            Check((await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized, "invalid token denied");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", first.AccessToken);
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized,
                "wrong authorization scheme denied"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.User!.Id);
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized,
                "user ID cannot serve as access token"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var meHttp = await client.GetAsync($"api/Auth/me?userId={second.User!.Id}");
            var me = (await meHttp.Content.ReadFromJsonAsync<CurrentUserResponse>())!;
            Check(
                me.Id == first.User!.Id && me.Name == "auth-first" && me.CurrentLobbyId == "" && me.CurrentRoomId == "" && me.PendingRoomId == "" && me.LobbyIndex == -1 && me.RoomIndex == -1,
                "current user query ignores supplied user ID and reports initial state"
            );
            Check(
                meHttp.Headers.CacheControl?.NoStore == true && meHttp.Headers.Pragma.Any(x => x.Name == "no-cache"),
                "current user response prevents caching"
            );
            var lobby = TestCatalogLobbies.Get(lobbies, "auth-lobby");
            // Deliberately inject an unknown userId field: the token must remain the sole caller identity.
            var entered = await client.PostAsJsonAsync("api/Lobby/join", new { UserId = second.User!.Id, LobbyId = lobby.Id });
            Check(
                entered.StatusCode == HttpStatusCode.OK && first.User.CurrentLobbyId == "",
                "HTTP join accepted with identity from token"
            );
            Check(
                users.FindById(first.User.Id)!.CurrentLobbyId == lobby.Id && users.FindById(second.User.Id)!.CurrentLobbyId == "",
                "spoofed body ID cannot move another user"
            );
            var joinedMe = await client.GetFromJsonAsync<CurrentUserResponse>("api/Auth/me");
            Check(
                joinedMe!.CurrentLobbyId == lobby.Id && joinedMe.CurrentLobbyName == lobby.Name && me.CurrentLobbyId == "",
                "current user query reflects lobby entry and previous snapshot stays unchanged"
            );
            var created = (await (await client.PostAsJsonAsync(
                "api/Room/create",
                new { UserId = second.User.Id, LobbyId = lobby.Id, RoomName = "auth-room", ContentId = "sample-training", ContentNodeId = 2, MaxUsers = 2 }
            )).Content.ReadFromJsonAsync<CreateRoomResponse>())!;
            var room = rooms.FindById(created.Room!.Id)!;
            Check(created.Success && room.MasterUserId == first.User.Id, "room creation uses token user as master");
            room.State = Smart_Training_Server.Enum.ServerState.Starting;
            room.ServerToken = "auth-server-secret";
            rooms.RegisterServer(room.Id, room.ServerToken, "auth-server");
            var admission = (await (await client.PostAsJsonAsync("api/Room/join", new { UserId = second.User.Id, LobbyId = lobby.Id, RoomId = room.Id })).Content.ReadFromJsonAsync<JoinRoomResponse>())!;
            Check(
                admission.Success && users.FindById(first.User.Id)!.PendingRoomId == room.Id && users.FindById(second.User.Id)!.PendingRoomId == "",
                "room reservation ignores spoofed body ID"
            );
            var pendingMe = await client.GetFromJsonAsync<CurrentUserResponse>("api/Auth/me");
            var meRaw = await client.GetStringAsync("api/Auth/me");
            Check(
                pendingMe!.PendingRoomId == room.Id && pendingMe.CurrentRoomId == "",
                "current user distinguishes pending reservation from confirmed room membership"
            );
            Check(
                !meRaw.Contains(first.AccessToken) && !meRaw.Contains(admission.AdmissionToken) && !meRaw.Contains(room.ServerToken) && !meRaw.Contains("\"accessToken\""),
                "current user query excludes all token values"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admission.AdmissionToken);
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized,
                "admission token cannot serve as user authentication"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", room.ServerToken);
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized,
                "server token cannot serve as user authentication"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            var cannotCancel = await client.PostAsJsonAsync("api/Room/leave", new { UserId = first.User.Id, LobbyId = lobby.Id, RoomId = room.Id });
            Check(
                cannotCancel.StatusCode == HttpStatusCode.BadRequest && users.FindById(first.User.Id)!.PendingRoomId == room.Id,
                "other user cannot cancel reservation by spoofing ID"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var cancel = await client.PostAsJsonAsync("api/Room/leave", new { lobbyId = lobby.Id, roomId = room.Id });
            Check(
                cancel.StatusCode == HttpStatusCode.OK && users.FindById(first.User.Id)!.PendingRoomId == "",
                "room cancellation works without body userId"
            );
            var leave = await client.PostAsJsonAsync("api/Lobby/leave", new { userId = second.User.Id, lobbyId = lobby.Id });
            Check(
                leave.StatusCode == HttpStatusCode.OK && users.FindById(first.User.Id)!.CurrentLobbyId == "",
                "lobby departure uses token user"
            );
            var leftMe = await client.GetFromJsonAsync<CurrentUserResponse>("api/Auth/me");
            Check(
                leftMe!.CurrentLobbyId == "" && leftMe.PendingRoomId == "" && leftMe.CurrentRoomId == "",
                "current user query reflects cancellation and lobby departure"
            );
            var swagger = await client.GetStringAsync("swagger/v1/swagger.json");
            using var document = JsonDocument.Parse(swagger);
            var paths = document.RootElement.GetProperty("paths");
            Check(
                paths.GetProperty("/api/Lobby").GetProperty("get").GetProperty("security").GetArrayLength() == 1 && !paths.GetProperty("/api/Auth/login").GetProperty("post").TryGetProperty("security", out _),
                "Swagger marks protected endpoints and anonymous login"
            );
            clock.Advance(TimeSpan.FromMinutes(1));
            Check(
                (await client.GetAsync("api/Auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "expired token cannot query current user during refresh grace"
            );
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized && users.FindByAccessToken(first.AccessToken) == null,
                "token denied at exact expiry"
            );
            var restarted = new UserService(Options.Create(new AuthOptions {
                        NameMinLength = 1,
                        NameMaxLength = 20
                    }));
            Check(restarted.FindByAccessToken(second.AccessToken) == null, "tokens invalid after server state reset");
            client.DefaultRequestHeaders.Authorization = null;
            Check(
                (await client.PostAsync("api/Auth/refresh", null)).StatusCode == HttpStatusCode.Unauthorized,
                "refresh requires token even though expired tokens are allowed"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admission.AdmissionToken);
            Check(
                (await client.PostAsync("api/Auth/refresh", null)).StatusCode == HttpStatusCode.Unauthorized,
                "admission token cannot refresh user session"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var refreshHttp = await client.PostAsync("api/Auth/refresh", null);
            var refreshed = (await refreshHttp.Content.ReadFromJsonAsync<LoginResponse>())!;
            Check(
                refreshHttp.StatusCode == HttpStatusCode.OK && refreshed.User!.Id == first.User.Id && refreshed.AccessToken != first.AccessToken && refreshHttp.Headers.CacheControl!.NoStore,
                "HTTP refresh within grace retains identity and rotates token"
            );
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized && !users.RefreshSession(first.AccessToken).Success,
                "old token invalid after rotation"
            );
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
            Check(
                (await client.GetFromJsonAsync<CurrentUserResponse>("api/Auth/me"))!.Id == first.User.Id,
                "refreshed token queries same current user"
            );
            Check(
                (await client.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.OK,
                "rotated token authenticates normally"
            );
            var simultaneous = await Task.WhenAll(
                Task.Run(() => users.RefreshSession(refreshed.AccessToken)),
                Task.Run(() => users.RefreshSession(refreshed.AccessToken))
            );
            Check(simultaneous.Count(x => x.Success) == 1, "only one concurrent token refresh succeeds");
            var current = simultaneous.Single(x => x.Success);
            var cleanupLobby = TestCatalogLobbies.Get(lobbies, "grace-lobby");
            await lobbies.JoinLobby(first.User.Id, new LobbyMembershipRequest { LobbyId = cleanupLobby.Id });
            await lobbies.JoinLobby(second.User.Id, new LobbyMembershipRequest { LobbyId = cleanupLobby.Id });
            var cleanupRoom = TestRoomSetup.Create(rooms,
                first.User.Id,
                new CreateRoomRequest {
                    LobbyId = cleanupLobby.Id,
                    RoomName = "grace-room",
                    ContentId = "sample-training", ContentNodeId = 2,
                    MaxUsers = 2
                }
            ).Room!;
            cleanupRoom.State = Smart_Training_Server.Enum.ServerState.Starting;
            cleanupRoom.ServerToken = "grace-server-secret";
            rooms.RegisterServer(cleanupRoom.Id, cleanupRoom.ServerToken, "grace-server");
            var firstJoin = await rooms.JoinRoom(first.User.Id, new JoinRoomRequest {
                    LobbyId = cleanupLobby.Id,
                    RoomId = cleanupRoom.Id
                });
            rooms.ConfirmConnection(cleanupRoom.Id, "grace-server", first.User.Id, firstJoin.AdmissionToken);
            var connectedMe = users.GetCurrentUser(first.User.Id)!;
            Check(
                connectedMe.CurrentRoomId == cleanupRoom.Id && connectedMe.CurrentRoomName == cleanupRoom.Name && connectedMe.PendingRoomId == "",
                "current user snapshot reflects confirmed room membership"
            );
            var secondJoin = await rooms.JoinRoom(second.User.Id, new JoinRoomRequest {
                    LobbyId = cleanupLobby.Id,
                    RoomId = cleanupRoom.Id
                });
            rooms.ConfirmConnection(cleanupRoom.Id, "grace-server", second.User.Id, secondJoin.AdmissionToken);
            var lifecycle = app.Services.GetRequiredService<UserLifecycleService>();
            Check(
                await lifecycle.CleanupExpiredUsersAsync() == 0 && cleanupRoom.Users.Count == 2,
                "grace period preserves users and established room connections"
            );
            clock.Advance(TimeSpan.FromMinutes(5)); // old second token expires at 1 minute; grace ends at 6.
            Check(!users.RefreshSession(second.AccessToken).Success, "refresh rejected at exact grace deadline");
            Check(
                await lifecycle.CleanupExpiredUsersAsync() == 1 && users.FindById(second.User.Id) == null && !cleanupRoom.Users.ContainsKey(second.User.Id),
                "cleanup removes expired user from active room and lobby"
            );
            Check(
                users.FindById(first.User.Id) != null && cleanupRoom.Users.ContainsKey(first.User.Id),
                "cleanup preserves refreshed user in grace"
            );
            Check(users.Login(new LoginRequest { UserName = "auth-second" }).Success, "cleanup releases nickname");
            Check(await lifecycle.CleanupExpiredUsersAsync() == 0, "cleanup is repeatable");
            clock.Advance(TimeSpan.FromMinutes(1));
            Check(!users.RefreshSession(current.AccessToken).Success, "rotated session cannot refresh after its grace");
            Check(
                await lifecycle.CleanupExpiredUsersAsync() == 1 && rooms.FindById(cleanupRoom.Id) == null && lobbies.FindById(cleanupLobby.Id) != null,
                "last expired user cleanup deletes room but retains catalog lobby"
            );
            var noGraceClock = new TestClock();
            var noGrace = new UserService(
                Options.Create(new AuthOptions {
                        NameMinLength = 1,
                        NameMaxLength = 20,
                        AccessTokenLifetimeMinutes = 1,
                        SessionGraceMinutes = 0
                    }),
                noGraceClock
            );
            var instant = noGrace.Login(new LoginRequest { UserName = "no-grace" });
            Check(noGrace.RefreshSession(instant.AccessToken).Success, "valid session can refresh before expiry");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
