using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

// 로비 입장 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Lobby
{
    public class JoinLobbyResponse : BaseResponse
    {
        // 처리 결과에 포함할 로비 정보입니다. 실패하면 null일 수 있습니다.
        public LobbyInfo? Lobby
        {
            get; set;
        }
    }
}
