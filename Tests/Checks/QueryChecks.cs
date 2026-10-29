using System.Net;
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
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class QueryChecks
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

        var clock = new TestClock();
        var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
        var lobbies = TestCatalogLobbies.Create(users, "query-lobby", "empty-lobby");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions { NameMinLength = 1, NameMaxLength = 20, MinUsers = 1, MaxUsers = 10 }),
            clock
        );
        var login = users.Login(new LoginRequest { UserName = "query-user" });
        var person = login.User!;
        var lobby = TestCatalogLobbies.Get(lobbies, "query-lobby");
        var empty = TestCatalogLobbies.Get(lobbies, "empty-lobby");
        await lobbies.JoinLobby(person.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        var room = TestRoomSetup.Create(
            rooms,
            person.Id,
            new CreateRoomRequest { LobbyId = lobby.Id, RoomName = "query-room", ContentId = "sample-training", ContentNodeId = 2, MaxUsers = 2 }
        ).Room!;
        room.ServerToken = "private-server-token";
        room.State = Smart_Training_Server.Enum.ServerState.Starting;
        Check(
            rooms.RegisterServer(room.Id, room.ServerToken, "query-server"),
            "prepare query server fixture"
        );
        var admission = await rooms.JoinRoom(person.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
        Check(admission.Success, "prepare query reservation");
        Check(
            rooms.GetRoomsByLobby(lobby.Id)!.Count == 0 && lobbies.GetLobbySummary(lobby.Id)!.RoomCount == 0,
            "reserved master room is hidden from public list and lobby count"
        );
        Check(
            rooms.GetOperationsRooms().Any(item => item.Id == room.Id),
            "operations retains unconfirmed room"
        );
        Check(
            !rooms.ConfirmConnection(room.Id, "query-server", person.Id, "wrong-token").Success &&
            rooms.GetRoomsByLobby(lobby.Id)!.Count == 0,
            "failed connection does not publish room"
        );
        Check(
            rooms.ConfirmConnection(
            room.Id,
            "query-server",
            person.Id,
            admission.AdmissionToken
        ).Success &&
            rooms.GetRoomsByLobby(lobby.Id)!.Count == 1,
            "master confirmed connection publishes room"
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
        builder.Services.AddControllers().AddApplicationPart(typeof(RoomController).Assembly);
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
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);
            var lobbyList = await client.GetFromJsonAsync<Smart_Training_Server.Responses.Lobby.LobbySummaryResponse[]>("api/Lobby");
            Check(
                lobbyList is { Length: 2 } && lobbyList.Any(x => x.Id == lobby.Id),
                "lobby summary list contains existing lobbies"
            );
            var lobbyDetail = await client.GetFromJsonAsync<Smart_Training_Server.Responses.Lobby.LobbySummaryResponse>($"api/Lobby/{lobby.Id}");
            Check(
                lobbyDetail!.UserCount == 1 && lobbyDetail.RoomCount == 1 && lobbyDetail.ContentCount == lobby.Contents.Count,
                "lobby summary reports membership and room and root content counts"
            );
            var lobbyRaw = await client.GetStringAsync("api/Lobby");
            Check(
                !lobbyRaw.Contains("private-server-token") &&
                !lobbyRaw.Contains(admission.AdmissionToken) &&
                !lobbyRaw.Contains("\"users\"") &&
                !lobbyRaw.Contains("\"rooms\"") &&
                !lobbyRaw.Contains("\"contents\""),
                "lobby list excludes nested internal objects and tokens"
            );
            var contentList = await client.GetFromJsonAsync<Smart_Training_Server.Responses.Lobby.ContentQueryResponse[]>($"api/Lobby/{lobby.Id}/contents");
            var expectedContent = lobby.Contents["sample-training"];
            var queriedContent = contentList!.Single(item => item.RootContent == "sample-training");
            Check(
                contentList.Length == lobby.Contents.Count &&
                queriedContent.Id == expectedContent.Id &&
                queriedContent.Children.Count == expectedContent.Children.Count,
                "content query preserves all roots and sample hierarchy"
            );
            var contentRaw = await client.GetStringAsync($"api/Lobby/{lobby.Id}/contents");
            Check(
                !contentRaw.Contains("\"path\"", StringComparison.OrdinalIgnoreCase),
                "content query excludes server file paths"
            );
            var contentSnapshot = lobbies.GetContentsByLobby(lobby.Id)!;
            var previousTitle = expectedContent.Title;
            expectedContent.Title = "changed-title";
            Check(
                contentSnapshot.Single(item => item.RootContent == "sample-training").Title == previousTitle &&
                lobbies.GetContentsByLobby(lobby.Id)!.Single(item => item.RootContent == "sample-training").Title == "changed-title",
                "content query returns independent snapshot"
            );
            expectedContent.Title = previousTitle;
            Check(
                (await client.GetAsync("api/Lobby/missing")).StatusCode == HttpStatusCode.NotFound &&
                (await client.GetAsync("api/Lobby/missing/contents")).StatusCode == HttpStatusCode.NotFound,
                "lobby and content query return 404 for missing lobby"
            );
            using (var anonymous = new HttpClient
            {
                BaseAddress = client.BaseAddress
            }

            )
            {
                Check(
                    (await anonymous.GetAsync("api/Lobby")).StatusCode == HttpStatusCode.Unauthorized &&
                    (await anonymous.GetAsync($"api/Lobby/{lobby.Id}")).StatusCode == HttpStatusCode.Unauthorized &&
                    (await anonymous.GetAsync($"api/Lobby/{lobby.Id}/contents")).StatusCode == HttpStatusCode.Unauthorized,
                    "all lobby queries require authentication"
                );
            }

            var emptySummary = await client.GetFromJsonAsync<Smart_Training_Server.Responses.Lobby.LobbySummaryResponse>($"api/Lobby/{empty.Id}");
            Check(
                emptySummary!.UserCount == 0 && emptySummary.RoomCount == 0,
                "empty lobby summary returns zero membership counts"
            );
            var list = await client.GetFromJsonAsync<RoomSummaryResponse[]>($"api/Lobby/{lobby.Id}/rooms");
            Check(
                list is { Length: 1 } && list[0].Id == room.Id && list[0].ContentId == "sample-training",
                "HTTP list includes only lobby rooms and content"
            );
            Check(
                list![0].ConnectedUsers == 1 && list[0].ReservedUsers == 0 && list[0].AvailableSlots == 1,
                "HTTP list distinguishes reservations from connections"
            );
            var detail = await client.GetFromJsonAsync<RoomSummaryResponse>($"api/Lobby/{lobby.Id}/rooms/{room.Id}");
            Check(
                detail!.Id == room.Id && detail.MaxUsers == 2 && detail.ContentTitle == room.Content.Title,
                "HTTP room detail"
            );
            var raw = await client.GetStringAsync($"api/Lobby/{lobby.Id}/rooms/{room.Id}");
            using var json = JsonDocument.Parse(raw);
            Check(
                !raw.Contains("private-server-token") &&
                !raw.Contains(admission.AdmissionToken) &&
                !json.RootElement.TryGetProperty("users", out _) &&
                !json.RootElement.TryGetProperty("serverToken", out _),
                "HTTP query excludes tokens and internal user objects"
            );
            var emptyResponse = await client.GetAsync($"api/Lobby/{empty.Id}/rooms");
            Check(
                emptyResponse.StatusCode == HttpStatusCode.OK &&
                (await emptyResponse.Content.ReadFromJsonAsync<RoomSummaryResponse[]>())!.Length == 0,
                "existing empty lobby returns 200 and empty array"
            );
            Check(
                (await client.GetAsync("api/Lobby/missing/rooms")).StatusCode == HttpStatusCode.NotFound,
                "missing lobby returns 404"
            );
            Check(
                (await client.GetAsync($"api/Lobby/{lobby.Id}/rooms/missing")).StatusCode == HttpStatusCode.NotFound,
                "missing room returns 404"
            );
            Check(
                (await client.GetAsync($"api/Lobby/{empty.Id}/rooms/{room.Id}")).StatusCode == HttpStatusCode.NotFound,
                "other lobby cannot resolve room"
            );
            Check(
                rooms.ConfirmConnection(
                room.Id,
                "query-server",
                person.Id,
                admission.AdmissionToken
            ).Success,
                "confirm connection for query"
            );
            var connected = await client.GetFromJsonAsync<RoomSummaryResponse>($"api/Lobby/{lobby.Id}/rooms/{room.Id}");
            Check(
                connected!.ConnectedUsers == 1 && connected.ReservedUsers == 0 && connected.AvailableSlots == 1,
                "query reflects confirmed membership"
            );
            Check(
                detail.ConnectedUsers == 1 && detail.ReservedUsers == 0,
                "previous connected query is stable snapshot"
            );
            var another = users.Login(new LoginRequest { UserName = "query-second" }).User!;
            await lobbies.JoinLobby(another.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
            var updatedLobby = await client.GetFromJsonAsync<Smart_Training_Server.Responses.Lobby.LobbySummaryResponse>($"api/Lobby/{lobby.Id}");
            Check(
                updatedLobby!.UserCount == 2 && lobbyDetail.UserCount == 1,
                "lobby query reflects join while previous snapshot stays unchanged"
            );
            var pending = await rooms.JoinRoom(another.Id, new JoinRoomRequest { LobbyId = lobby.Id, RoomId = room.Id });
            Check(
                pending.Success && rooms.GetRoomByLobby(lobby.Id, room.Id)!.AvailableSlots == 0,
                "reserved slot fills capacity"
            );
            clock.Advance(TimeSpan.FromSeconds(31));
            var expired = await client.GetFromJsonAsync<RoomSummaryResponse>($"api/Lobby/{lobby.Id}/rooms/{room.Id}");
            Check(
                expired!.ReservedUsers == 0 && expired.AvailableSlots == 1 && another.PendingRoomId == "",
                "query cleans expired reservations"
            );
            rooms.ClientDisconnected(room.Id, "query-server", person.Id, admission.AdmissionToken);
            Check(
                (await client.GetAsync($"api/Lobby/{lobby.Id}/rooms/{room.Id}")).StatusCode == HttpStatusCode.NotFound,
                "deleted room returns 404"
            );
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
