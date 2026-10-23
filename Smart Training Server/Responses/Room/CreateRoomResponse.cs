using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

// 방 생성 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Room
{
    public class CreateRoomResponse : BaseResponse
    {
        // 처리 결과에 포함할 방 정보입니다. 실패하면 null일 수 있습니다.
        public RoomInfo? Room
        {
            get; set;
        }
    }
}
