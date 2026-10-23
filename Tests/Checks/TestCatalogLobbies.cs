using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;

// Isolated room/auth fixtures use catalog initialization too. They never need a production lobby-create API.
internal static class TestCatalogLobbies
{
    public static LobbyService Create(UserService users, params string[] names)
    {
        var folder = Path.Combine(Path.GetTempPath(), "training-catalog-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "catalog.json"),
                JsonSerializer.Serialize(names.Distinct().Select(name => new ContentInfo {
                            Id = 1,
                            RootContent = name,
                            Title = name
                        }))
            );
            var service = FromCatalog(new ContentService(folder), users);
            // These fixtures isolate room/auth rules and intentionally provide the sample content set.
            // CatalogLobbyChecks separately checks the real one-root-per-lobby production mapping.
            foreach (var lobby in service.GetAll())
            {
                lobby.Contents = new ContentService().LoadLobbyContents();
            }

            return service;
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    public static LobbyService FromCatalog(ContentService catalog, UserService users)
    {
        var service = new LobbyService(catalog, users);
        service.InitializeCatalogLobbies();
        return service;
    }

    public static LobbyInfo Get(LobbyService service, string id) => service.FindById(id) ?? throw new InvalidOperationException("Missing fixture catalog lobby: " + id);

}
