using Smart_Training_Server.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Smart_Training_Server.Services;

public class ContentService
{
    private readonly string catalogPath;
    private readonly ILogger<ContentService>? logger;

    public ContentService(string? contentsDirectory = null, ILogger<ContentService>? logger = null)
    {
        catalogPath = Path.Combine(contentsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Contents"), "catalog.json");
        this.logger = logger;
    }

    public ConcurrentDictionary<string, ContentInfo> LoadLobbyContents()
    {
        try
        {
            if (!File.Exists(catalogPath)) throw new InvalidDataException("Contents/catalog.json 파일이 없습니다.");
            var roots = JsonSerializer.Deserialize<List<ContentInfo>>(File.ReadAllText(catalogPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("콘텐츠 목록은 JSON 배열이어야 합니다.");
            if (roots.Count == 0) throw new InvalidDataException("콘텐츠 목록이 비어 있습니다.");
            var contents = new ConcurrentDictionary<string, ContentInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (root == null || string.IsNullOrWhiteSpace(root.RootContent))
                    throw new InvalidDataException("콘텐츠의 RootContent가 필요합니다.");
                root.RootContent = root.RootContent.Trim();
                ValidateTree(root, root.RootContent, 0, 0, new HashSet<int>());
                if (!contents.TryAdd(root.RootContent, root)) throw new InvalidDataException($"중복 콘텐츠 ID: {root.RootContent}");
            }
            return contents;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger?.LogError(ex, "콘텐츠 목록을 읽지 못했습니다.");
            throw new InvalidDataException("콘텐츠 목록을 읽지 못했습니다. Contents/catalog.json을 확인해주세요.", ex);
        }
    }

    private static void ValidateTree(ContentInfo node, string rootContent, int depth, int parentId, HashSet<int> ids)
    {
        if (string.IsNullOrWhiteSpace(node.Title)) throw new InvalidDataException("콘텐츠 제목이 필요합니다.");
        if (node.Id <= 0 || !ids.Add(node.Id)) throw new InvalidDataException("트리 안의 Id는 서로 다른 양수여야 합니다.");
        if (node.Children == null) throw new InvalidDataException("Children은 배열이어야 합니다.");
        node.Title = node.Title.Trim();
        node.RootContent = rootContent;
        node.Depth = depth;
        node.ParentId = parentId;
        foreach (var child in node.Children)
        {
            if (child == null) throw new InvalidDataException("Children에 null을 넣을 수 없습니다.");
            ValidateTree(child, rootContent, depth + 1, node.Id, ids);
        }
    }
}