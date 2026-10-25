// catalog.json의 콘텐츠 트리 한 노드를 표현합니다. 로딩 시 부모·깊이를 계산하고 ID와 제목을 검사합니다.
namespace Smart_Training_Server.Models
{
    // catalog.json의 콘텐츠 한 노드입니다. Children으로 하위 콘텐츠를 연결하는 트리 구조입니다.
    public class ContentInfo
    {
        // 최상위 콘텐츠를 구분하는 문자열 ID입니다. 고정 로비 ID에도 사용합니다.
        public string RootContent { get; set; } = string.Empty;

        // 트리 깊이입니다. 최상위는 0이며 로딩 시 계산합니다.
        public int Depth { get; set; } = 0;

        // 같은 콘텐츠 트리 안에서 노드를 구분하는 양수 ID입니다.
        public int Id { get; set; }

        // 부모 노드 ID입니다. 로딩 과정에서 부모 관계에 맞게 설정합니다.
        public int ParentId { get; set; }

        // 로비나 콘텐츠 목록에 표시할 제목입니다.
        public string Title { get; set; } = string.Empty;

        // 콘텐츠 경로 정보입니다. null을 허용하며 실제 활용은 콘텐츠 구현에 따라 확장할 수 있습니다.
        public string? Path { get; set; }

        // 하위 콘텐츠들입니다. 자식이 없으면 빈 배열로 작성합니다.
        public List<ContentInfo> Children { get; set; } = new();
    }
}
