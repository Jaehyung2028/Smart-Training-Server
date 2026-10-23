using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Api;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Operations;
using Smart_Training_Server.Services.Room;

internal static class OperatingModeApiChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new InvalidOperationException(name);
            }

            Console.WriteLine("PASS: mode HTTP " + name);
        }

        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var registry = new ServerOperationsRegistry();
        var lobbies = new LobbyService(new ContentService(), users, registry);
        lobbies.InitializeCatalogLobbies();
        var rooms = new RoomService(lobbies, users, Options.Create(new RoomOptions { MinUsers = 1, MaxUsers = 4, NameMinLength = 1, NameMaxLength = 20 }), operations: registry);
        var modes = new LobbyOperatingModeService(users, lobbies, rooms, registry);
        var member = users.Login(new LoginRequest { UserName = "mode-member" }).User!;
        await lobbies.JoinLobby(member.Id, new LobbyMembershipRequest { LobbyId = "sample-training" });
        var newcomer = users.Login(new LoginRequest { UserName = "mode-new" }).User!;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ServerOperationsService(users, lobbies, rooms, registry));
        builder.Services.AddSingleton(modes);
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddTrainingApiControllers().AddApplicationPart(typeof(OperationsController).Assembly);
        await using var app = builder.Build();
        app.UseTrainingApiErrors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            const string path = "/api/operations/lobbies/sample-training/mode";
            async Task<bool> Post(object body, HttpStatusCode expected)
            {
                using var response = await client.PostAsJsonAsync(path, body);
                return response.StatusCode == expected;
            }

            var maintenance = new { mode = "maintenance", reason = "점검", expectedRevision = 0 };
            Check(await Post(maintenance, HttpStatusCode.Forbidden), "custom operation header is required");
            client.DefaultRequestHeaders.Add("X-Operations-Request", "1");
            client.DefaultRequestHeaders.Add("Origin", "https://other.example");
            Check(await Post(maintenance, HttpStatusCode.Forbidden), "foreign origin cannot mutate unified mode");
            client.DefaultRequestHeaders.Remove("Origin");
            Check(await Post(maintenance, HttpStatusCode.OK) && registry.GetOperatingMode("sample-training").Mode == "maintenance", "one mode endpoint enables maintenance");
            Check(member.CurrentLobbyId == "sample-training" && !(await lobbies.JoinLobby(newcomer.Id, new LobbyMembershipRequest { LobbyId = "sample-training" })).Success, "maintenance preserves member and blocks new entry");
            Check(await Post(maintenance, HttpStatusCode.OK) && registry.GetOperatingMode("sample-training").Revision == 1, "duplicate request remains idempotent");
            Check(await Post(new { mode = "running", reason = "오래된 요청", expectedRevision = 0 }, HttpStatusCode.Conflict), "stale revision cannot cancel newer maintenance");
            Check(await Post(new { mode = "scheduled", reason = "시각 누락", expectedRevision = 1 }, HttpStatusCode.BadRequest), "scheduled mode requires deadline");
            Check(await Post(new { mode = "maintenance", reason = "잘못된 시각", atUtc = DateTimeOffset.UtcNow.AddMinutes(1), expectedRevision = 1 }, HttpStatusCode.BadRequest), "maintenance mode rejects deadline");
            Check(await Post(new { mode = "unknown", reason = "알 수 없는 모드", expectedRevision = 1 }, HttpStatusCode.BadRequest), "unknown mode is rejected");
            Check(await Post(new { reason = "모드 누락", expectedRevision = 1 }, HttpStatusCode.BadRequest), "mode is required by request validation");
            Check(await Post(new { mode = "running", reason = "버전 누락" }, HttpStatusCode.BadRequest), "expected revision remains required");
            Check(await Post(new { mode = "running", reason = " ", expectedRevision = 1 }, HttpStatusCode.BadRequest), "blank change reason is rejected");
            Check(await Post(new { mode = "running", reason = "음수 버전", expectedRevision = -1 }, HttpStatusCode.BadRequest), "negative revision is rejected");
            Check(await Post(new { mode = "scheduled", reason = "과거", atUtc = DateTimeOffset.UtcNow.AddMinutes(-1), expectedRevision = 1 }, HttpStatusCode.BadRequest), "past deadline is rejected");
            var due = DateTimeOffset.UtcNow.AddMinutes(5);
            Check(await Post(new { mode = "scheduled", reason = "종료 예약", atUtc = due, expectedRevision = 1 }, HttpStatusCode.OK), "one mode endpoint creates shutdown schedule");
            Check(await Post(new { mode = "maintenance", reason = "예약 취소 후 점검", expectedRevision = 2 }, HttpStatusCode.OK) && registry.GetOperatingMode("sample-training").ShutdownAtUtc == null, "switching scheduled to maintenance removes deadline");
            modes.ExecuteDue(due.UtcDateTime.AddSeconds(1));
            Check(member.CurrentLobbyId == "sample-training" && registry.GetOperatingMode("sample-training").Revision == 3, "cancelled deadline cannot later evacuate members");
            Check(await Post(new { mode = "scheduled", reason = "실제 종료", atUtc = due, expectedRevision = 3 }, HttpStatusCode.OK), "maintenance can become scheduled again");
            modes.ExecuteDue(due.UtcDateTime);
            Check(member.CurrentLobbyId == "" && registry.GetOperatingMode("sample-training") is { Mode: "maintenance", Revision: 5 }, "deadline evacuates members and stays in maintenance");
            Check(await Post(new { mode = "running", reason = "운영 재개", expectedRevision = 5 }, HttpStatusCode.OK) && registry.GetOperatingMode("sample-training").ShutdownAtUtc == null && (await lobbies.JoinLobby(member.Id, new LobbyMembershipRequest { LobbyId = "sample-training" })).Success, "resume clears schedule and permits entry");
            using var removedMaintenance = await client.PostAsJsonAsync("/api/operations/lobbies/sample-training/maintenance", maintenance);
            using var removedShutdown = await client.PostAsJsonAsync("/api/operations/lobbies/sample-training/shutdown", new { atUtc = due, reason = "old", expectedRevision = 6 });
            Check(removedMaintenance.StatusCode == HttpStatusCode.NotFound && removedShutdown.StatusCode == HttpStatusCode.NotFound, "obsolete mutation endpoints are absent");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
