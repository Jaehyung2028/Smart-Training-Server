using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;

internal static class CatalogLobbyChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name);
            }

            Console.WriteLine("PASS: catalog " + name);
        }

        var folder = Path.Combine(Path.GetTempPath(), "catalog-direct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "catalog.json"),
                "[{\"rootContent\":\"alpha\",\"id\":1,\"title\":\"Alpha\"},{\"rootContent\":\"beta\",\"id\":2,\"title\":\"Beta\"}]");
            var users = new UserService(Options.Create(new AuthOptions { NameMinLength = 1, NameMaxLength = 20 }));
            var service = new LobbyService(new ContentService(folder), users);
            var a = users.Login(new LoginRequest { UserName = "a" }).User!;
            var b = users.Login(new LoginRequest { UserName = "b" }).User!;
            Check(!(await service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "alpha" })).Success, "uninitialized catalog rejects entry");
            service.InitializeCatalogLobbies();
            service.InitializeCatalogLobbies();
            Check(service.GetLobbySummaries().Count == 2, "catalog roots create fixed lobbies once");
            Check(service.FindById("alpha")!.Name == "Alpha" && service.GetContentsByLobby("alpha")!.Single().RootContent == "alpha", "each lobby exposes its own root");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Check(!(await service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "alpha" }, cancellation.Token)).Success && a.CurrentLobbyId == "", "cancel before entry leaves membership unchanged");
            Check(!(await service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "missing" })).Success, "unknown lobby rejected");
            var joined = await Task.WhenAll(
                Task.Run(() => service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "alpha" })),
                Task.Run(() => service.JoinLobby(b.Id, new LobbyMembershipRequest { LobbyId = "alpha" })));
            Check(joined.All(x => x.Success) && service.FindById("alpha")!.Users.Count == 2, "concurrent members join without executable or readiness registration");
            Check(a.CurrentLobbyId == "alpha" && users.GetCurrentUser(a.Id)!.CurrentLobbyId == "alpha", "entry immediately updates authoritative user membership");
            Check(!(await service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "beta" })).Success && a.CurrentLobbyId == "alpha", "duplicate and foreign entry preserve original membership");
            Check(!service.LeaveLobby(a.Id, new LobbyMembershipRequest { LobbyId = "beta" }).Success, "foreign lobby departure rejected");
            Check(service.LeaveLobby(a.Id, new LobbyMembershipRequest { LobbyId = "alpha" }).Success && a.CurrentLobbyId == "", "departure clears membership");
            Check(service.LeaveLobby(b.Id, new LobbyMembershipRequest { LobbyId = "alpha" }).Success && service.GetLobbySummaries().Count == 2 && service.FindById("alpha")!.Users.IsEmpty, "empty catalog lobbies stay listed without workers");
            var race = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => service.JoinLobby(a.Id, new LobbyMembershipRequest { LobbyId = "beta" }))));
            Check(race.Count(x => x.Success) == 1 && service.FindById("beta")!.Users.Count == 1, "concurrent duplicate requests establish one membership");
            var json = JsonSerializer.Serialize(service.GetLobbySummary("alpha"));
            Check(!json.Contains("ServerState") && !json.Contains("PendingUsers") && !json.Contains("Executable") && !json.Contains("ServerToken"), "lobby summary excludes obsolete worker and transport fields");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
