// 로비 참가자 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
// init 전용 조회 데이터이므로 record의 값 비교를 사용합니다. JSON 구조는 기존과 같습니다.
namespace Smart_Training_Server.Responses.Lobby
{
    public sealed record LobbyParticipantResponse
    {
        // 처리 대상 사용자 ID입니다. 요청자의 권한은 서버의 인증 정보로 별도 확인합니다.
        public string UserId { get; init; } = "";

        // 참가자 목록에 표시할 닉네임입니다.
        public string Nickname { get; init; } = "";

        // 참가자가 로비 대기·방 예약·방 접속 중 어느 상태인지 나타냅니다.
        public string ConnectionState { get; init; } = "lobby";

        // 대상 또는 소속 방의 ID입니다.
        public string RoomId { get; init; } = "";

        // 방의 표시 이름입니다.
        public string RoomName { get; init; } = "";

        // 이 참가자가 현재 방장인지 나타냅니다.
        public bool IsRoomMaster
        {
            get; init;
        }

        // 참가자가 속한 방이 훈련 진행 중인지 나타냅니다.
        public bool RoomIsRunning
        {
            get; init;
        }
    }
}
