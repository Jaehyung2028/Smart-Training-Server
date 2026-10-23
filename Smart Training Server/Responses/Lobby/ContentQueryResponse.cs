// 공개 콘텐츠 트리 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Lobby
{
    public class ContentQueryResponse
    {
        // 최상위 콘텐츠를 구분하는 ID입니다.
        public string RootContent { get; init; } = string.Empty;

        // 이 객체의 고유 식별자입니다. 이름이 바뀌어도 ID를 기준으로 찾습니다.
        public int Id
        {
            get; init;
        }

        // 트리에서 부모 콘텐츠의 ID입니다.
        public int ParentId
        {
            get; init;
        }

        // 콘텐츠 트리 깊이입니다. 최상위는 0입니다.
        public int Depth
        {
            get; init;
        }

        // 콘텐츠의 표시 제목입니다.
        public string Title { get; init; } = string.Empty;

        // 하위 콘텐츠 목록입니다. 조회 응답도 같은 구조를 재귀적으로 사용합니다.
        public IReadOnlyList<ContentQueryResponse> Children { get; init; } = Array.Empty<ContentQueryResponse>();
    }
}
