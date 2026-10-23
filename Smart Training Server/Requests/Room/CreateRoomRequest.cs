// 방 생성 요청 객체입니다. JSON 본문을 이 형식으로 받은 뒤 Controller와 Service에서 필수값·권한·업무 조건을 확인합니다.
namespace Smart_Training_Server.Requests.Room
{
    public class CreateRoomRequest
    {
        // 요청자 ID는 JSON에 넣지 않습니다. Controller가 토큰으로 확인한 ID를 서비스 인자로 따로 전달합니다.
        // 방 입장·설정에 사용하는 비밀번호 입력입니다. 구체적인 빈 값 처리 규칙은 담당 서비스에서 검사합니다.
        public string? Password
        {
            get; set;
        }

        // 대상 또는 소속 로비의 ID입니다.
        public string LobbyId { get; set; } = string.Empty;

        // 방의 표시 이름입니다.
        public string RoomName { get; set; } = string.Empty;

        // 카탈로그의 최상위 콘텐츠 ID입니다.
        public string ContentId { get; set; } = string.Empty;

        // 방 최대 정원입니다. 실제 인원과 입장 예약을 포함한 조건은 서비스에서 검사합니다.
        public int MaxUsers
        {
            get; set;
        }
    }
}
