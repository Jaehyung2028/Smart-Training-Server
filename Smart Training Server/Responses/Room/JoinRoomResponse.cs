using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Base;

// 방 입장 예약 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Room
{
    public class JoinRoomResponse : BaseResponse
    {
        // 처리 결과에 포함할 방 정보입니다. 실패하면 null일 수 있습니다.
        public RoomInfo? Room
        {
            get; set;
        }

        // 해당 방의 Unity 서버 연결 시 제시할 예약 토큰입니다. 로그인 인증 토큰과 다릅니다.
        public string AdmissionToken { get; set; } = string.Empty;

        // 토큰 또는 입장 예약이 만료되는 UTC 시각입니다. 이 응답이 어느 토큰을 다루는지 함께 확인합니다.
        public DateTime? ExpiresAtUtc
        {
            get; set;
        }
    }
}
