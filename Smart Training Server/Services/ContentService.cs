using System.Collections.Concurrent;
using System.Text.Json;
using Smart_Training_Server.Models;

// 콘텐츠 JSON을 읽고 트리 구조를 검사합니다. 최상위 콘텐츠로 고정 로비를 구성하며, Unity 방 서버 실행은 RoomService가 담당합니다.
namespace Smart_Training_Server.Services
{
    public class ContentService
    {
        private readonly string catalogPath;

        private readonly ILogger<ContentService>? logger;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public ContentService(string? contentsDirectory = null, ILogger<ContentService>? logger = null)
        {
            catalogPath = Path.Combine(contentsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Contents"), "catalog.json");
            this.logger = logger;
        }

        // catalog.json을 읽고 ID·제목·하위 콘텐츠 구조를 검사합니다. 잘못된 카탈로그는 시작 단계에서 오류로 알립니다.
        public ConcurrentDictionary<string, ContentInfo> LoadLobbyContents()
        {
            try
            {
                if (!File.Exists(catalogPath))
                {
                    throw new InvalidDataException("Contents/catalog.json 파일이 없습니다.");
                }

                var roots = JsonSerializer.Deserialize<List<ContentInfo>>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("콘텐츠 목록은 JSON 배열이어야 합니다.");
                if (roots.Count == 0)
                {
                    throw new InvalidDataException("콘텐츠 목록이 비어 있습니다.");
                }

                var contents = new ConcurrentDictionary<string, ContentInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in roots)
                {
                    if (root == null || string.IsNullOrWhiteSpace(root.RootContent))
                    {
                        throw new InvalidDataException("콘텐츠의 RootContent가 필요합니다.");
                    }

                    root.RootContent = root.RootContent.Trim();
                    ValidateTree(root, root.RootContent, 0, 0, new HashSet<int>());
                    if (!contents.TryAdd(root.RootContent, root))
                    {
                        throw new InvalidDataException($"중복 콘텐츠 ID: {root.RootContent}");
                    }
                }

                return contents;
            }
            catch (Exception ex)when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                logger?.LogError(ex, "콘텐츠 목록을 읽지 못했습니다.");
                throw new InvalidDataException("콘텐츠 목록을 읽지 못했습니다. Contents/catalog.json을 확인해주세요.", ex);
            }
        }

        // 자식 콘텐츠까지 재귀적으로 검사하면서 깊이와 부모 ID를 계산합니다.
        private static void ValidateTree(ContentInfo node, string rootContent, int depth, int parentId, HashSet<int> ids)
        {
            if (string.IsNullOrWhiteSpace(node.Title))
            {
                throw new InvalidDataException("콘텐츠 제목이 필요합니다.");
            }

            if (node.Id <= 0 || !ids.Add(node.Id))
            {
                throw new InvalidDataException("트리 안의 Id는 서로 다른 양수여야 합니다.");
            }

            if (node.Children == null)
            {
                throw new InvalidDataException("Children은 배열이어야 합니다.");
            }

            // 기존 카탈로그에 타입이 없으면 부모는 category, 최하위는 model로 보완합니다.
            // 명시한 타입이 잘못됐거나 분류 구조와 맞지 않으면 서버 시작 시 오류를 알립니다.
            node.ContentType = string.IsNullOrWhiteSpace(node.ContentType)
                ? (node.Children.Count > 0 ? "category" : "model")
                : node.ContentType.Trim().ToLowerInvariant();
            if (node.ContentType is not ("category" or "video" or "scenario" or "model"))
            {
                throw new InvalidDataException($"지원하지 않는 콘텐츠 타입: {node.ContentType} (노드 {node.Id})");
            }

            if ((node.Children.Count > 0) != (node.ContentType == "category"))
            {
                throw new InvalidDataException($"부모 콘텐츠는 category, 최하위 콘텐츠는 video, scenario 또는 model이어야 합니다. (노드 {node.Id})");
            }

            node.AssetId = node.AssetId?.Trim() ?? string.Empty;
            if (node.ContentType == "category" && node.AssetId.Length > 0)
            {
                throw new InvalidDataException($"분류 콘텐츠에는 AssetId를 지정할 수 없습니다. (노드 {node.Id})");
            }

            node.Title = node.Title.Trim();
            node.RootContent = rootContent;
            node.Depth = depth;
            node.ParentId = parentId;
            foreach (var child in node.Children)
            {
                if (child == null)
                {
                    throw new InvalidDataException("Children에 null을 넣을 수 없습니다.");
                }

                ValidateTree(child, rootContent, depth + 1, node.Id, ids);
            }
        }
    }
}
