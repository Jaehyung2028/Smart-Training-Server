using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class ContentChecks
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

        var services = new ServiceCollection().AddLogging().AddSingleton<ContentService>();
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<ContentService>();
        var roots = catalog.LoadLobbyContents();
        Check(roots.ContainsKey("sample-training"), "published sample catalog loads via DI");
        var root = roots["sample-training"];
        Check(
            root.Depth == 0 && root.ParentId == 0 && root.Children[0].Depth == 1 && root.Children[0].ParentId == root.Id && root.Children[0].RootContent == root.RootContent,
            "normalize content tree hierarchy"
        );
        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.FromCatalog(catalog, users);
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
        var user = users.Login(new LoginRequest { UserName = "content-user" }).User!;
        var first = TestCatalogLobbies.Get(lobbies, "sample-training");
        var metadata = System.Text.Json.JsonSerializer.SerializeToElement(first, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Check(
            first.Contents.ContainsKey("sample-training") && !metadata.TryGetProperty("ipAddress", out _) && !metadata.TryGetProperty("port", out _) && !metadata.TryGetProperty("state", out _),
            "lobby initializes metadata without obsolete Unity server fields"
        );
        Check(
            (await lobbies.JoinLobby(user.Id, new LobbyMembershipRequest { LobbyId = first.Id })).Success,
            "enter content lobby"
        );
        var response = TestRoomSetup.Create(rooms,
            user.Id,
            new CreateRoomRequest {
                LobbyId = first.Id,
                RoomName = "content-room",
                ContentId = "sample-training",
                MaxUsers = 2
            }
        );
        Check(
            response.Success && response.Room!.Content.Title == root.Title,
            "create room from automatically loaded content"
        );
        Check(
            !TestRoomSetup.Create(rooms,
                user.Id,
                new CreateRoomRequest {
                    LobbyId = first.Id,
                    RoomName = "bad-room",
                    ContentId = "missing-content",
                    MaxUsers = 2
                }
            ).Success,
            "reject nonexistent content"
        );
        var secondService = TestCatalogLobbies.FromCatalog(catalog, users);
        var second = TestCatalogLobbies.Get(secondService, "sample-training");
        first.Contents["sample-training"].Title = "changed";
        Check(second.Contents["sample-training"].Title == root.Title, "each lobby receives independent content objects");
        var directory = Path.Combine(AppContext.BaseDirectory, "content-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var invalidCatalog = new ContentService(directory);
        var invalidLobbies = new LobbyService(invalidCatalog, users);
        bool MissingCatalogRejected()
        {
            try
            {
                invalidLobbies.InitializeCatalogLobbies();
                return false;
            }
            catch (InvalidDataException)
            {
                return !invalidLobbies.GetAll().Any();
            }
        }

        Check(MissingCatalogRejected(), "missing catalog does not register lobby");
        string[] invalid = ["{", "null", "[]", "[null]", "[{\"rootContent\":\"a\",\"id\":1}]", "[{\"rootContent\":\" \",\"id\":1,\"title\":\"title\"}]", "[{\"rootContent\":\"a\",\"id\":0,\"title\":\"title\"}]", "[{\"rootContent\":\"a\",\"id\":1,\"title\":\"t\",\"children\":null}]", "[{\"rootContent\":\"a\",\"id\":1,\"title\":\"t\",\"children\":[null]}]", "[{\"rootContent\":\"a\",\"id\":1,\"title\":\"t\",\"children\":[{\"id\":1,\"title\":\"c\"}]}]", "[{\"rootContent\":\"a\",\"id\":1,\"title\":\"t\"},{\"rootContent\":\" A \",\"id\":2,\"title\":\"t\"}]"];
        foreach (var item in invalid)
        {
            File.WriteAllText(Path.Combine(directory, "catalog.json"), item);
            Check(MissingCatalogRejected(), "invalid JSON or tree does not register lobby");
        }

        File.WriteAllText(
            Path.Combine(directory, "catalog.json"),
            "[{\"rootContent\":\" restored \",\"id\":1,\"title\":\" restored \"}]"
        );
        invalidLobbies.InitializeCatalogLobbies();
        Check(
            invalidLobbies.FindById("restored")?.Contents.ContainsKey("restored") == true,
            "catalog correction allows retry"
        );
    }
}
