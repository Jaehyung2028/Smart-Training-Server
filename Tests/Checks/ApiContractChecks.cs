using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Responses.Auth;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Api;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class ApiContractChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            var raw = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            if (response.StatusCode != status || root.GetProperty("errorCode").GetString() != code)
                throw new Exception($"Unexpected error response: {(int)response.StatusCode} {raw}");
            Check(response.StatusCode == status && !root.GetProperty("success").GetBoolean() &&
                root.GetProperty("errorCode").GetString() == code && !string.IsNullOrEmpty(root.GetProperty("message").GetString()) &&
                !string.IsNullOrEmpty(root.GetProperty("traceId").GetString()), "uniform HTTP error " + code);
            Check(!raw.Contains("sensitive-test-exception") && !raw.Contains("stackTrace"), "error hides internal exception details");
        }
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = new LobbyService(new ContentService(), users, Options.Create(new LobbyOptions { MaxCount = 10, NameMinLength = 1, NameMaxLength = 20 }));
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 10, NameMinLength = 1, NameMaxLength = 20, ServerExecutablePath = "missing-test-server.exe" }));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(users).AddSingleton(lobbies).AddSingleton(rooms).AddSingleton<UserLifecycleService>();
        builder.Services.AddSignalR();
        builder.Services.AddAuthentication(UserTokenAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserTokenAuthenticationHandler>(UserTokenAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("TestForbidden", policy => policy.RequireClaim("absent-claim")));
        builder.Services.AddTrainingApiControllers().AddApplicationPart(typeof(RoomController).Assembly);
        await using var app = builder.Build();
        app.UseTrainingApiErrors(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapGet("/api/test-throw", () => { throw new InvalidOperationException("sensitive-test-exception"); }).RequireAuthorization();
        app.MapGet("/api/test-forbidden", () => "unreachable").RequireAuthorization("TestForbidden");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await Error(await client.PostAsJsonAsync("api/Lobby/create", new { lobbyName = "standard" }), HttpStatusCode.Unauthorized, "unauthorized");
            await Error(await client.PostAsync("api/Auth/refresh", null), HttpStatusCode.Unauthorized, "unauthorized");
            var login = (await (await client.PostAsJsonAsync("api/Auth/login", new { userName = "contract-user" })).Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var lobbyResponse = await client.PostAsJsonAsync("api/Lobby/create", new { lobbyName = "standard" });
            var lobby = (await lobbyResponse.Content.ReadFromJsonAsync<CreateLobbyResponse>())!.Lobby!;
            Check(lobbyResponse.StatusCode == HttpStatusCode.OK && lobby.Contents.ContainsKey("sample-training"), "standard lobby create route");
            Check((await client.PostAsJsonAsync("api/Lobby/creat", new { lobbyName = "legacy" })).StatusCode == HttpStatusCode.OK, "legacy lobby route remains usable");
            Check((await client.PostAsJsonAsync("api/Lobby/join", new { lobbyId = lobby.Id })).StatusCode == HttpStatusCode.OK, "full flow lobby entry without userId");
            var createdHttp = await client.PostAsJsonAsync("api/Room/create", new { lobbyId = lobby.Id, roomName = "standard-room", contentId = "sample-training", maxUsers = 2 });
            var room = (await createdHttp.Content.ReadFromJsonAsync<CreateRoomResponse>())!.Room!;
            Check(createdHttp.StatusCode == HttpStatusCode.OK, "standard room create route");
            Check((await client.PostAsJsonAsync("api/Room/creat", new { lobbyId = lobby.Id, roomName = "legacy-room", contentId = "sample-training", maxUsers = 2 })).StatusCode == HttpStatusCode.OK, "legacy room route remains usable");
            Check((await client.GetFromJsonAsync<RoomSummaryResponse[]>($"api/Lobby/{lobby.Id}/rooms"))!.Length == 2, "full flow room list");
            await Error(await client.PostAsJsonAsync("api/Room/create", new { lobbyId = lobby.Id, roomName = "bad-content", contentId = "missing", maxUsers = 2 }), HttpStatusCode.BadRequest, "bad_request");
            await Error(await client.PostAsJsonAsync("api/Room/join", new { lobbyId = lobby.Id, roomId = room.Id }), HttpStatusCode.BadRequest, "bad_request");
            Check(users.FindById(login.User!.Id)!.PendingRoomId == "", "missing Unity executable does not strand reservation");
            await Error(await client.PostAsync("api/Lobby/create", new StringContent("{", Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "validation_failed");
            await Error(await client.PostAsync("api/Room/join", new StringContent("", Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "validation_failed");
            await Error(await client.PostAsync("api/Lobby/create", new StringContent("plain", Encoding.UTF8, "text/plain")), HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
            await Error(await client.GetAsync($"api/Lobby/{lobby.Id}/rooms/missing"), HttpStatusCode.NotFound, "not_found");
            await Error(await client.GetAsync("api/nonexistent"), HttpStatusCode.NotFound, "not_found");
            await Error(await client.GetAsync("api/Auth/logout"), HttpStatusCode.MethodNotAllowed, "method_not_allowed");
            await Error(await client.GetAsync("api/test-forbidden"), HttpStatusCode.Forbidden, "forbidden");
            await Error(await client.GetAsync("api/test-throw"), HttpStatusCode.InternalServerError, "internal_error");
            var renewed = (await (await client.PostAsync("api/Auth/refresh", null)).Content.ReadFromJsonAsync<LoginResponse>())!;
            Check(renewed.Success && renewed.User!.Id == login.User.Id, "full flow refresh preserves identity");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed.AccessToken);
            Check((await client.PostAsync("api/Auth/logout", null)).StatusCode == HttpStatusCode.OK && rooms.FindById(room.Id) == null && lobbies.FindById(lobby.Id) == null, "full flow logout cleans owned rooms and lobby");
            await Error(await client.GetAsync("api/Lobby"), HttpStatusCode.Unauthorized, "unauthorized");
            Check((await client.PostAsJsonAsync("api/Auth/login", new { userName = "contract-user" })).StatusCode == HttpStatusCode.OK, "full flow nickname reusable after logout");
        }
        finally { await app.StopAsync(); }
    }
}
