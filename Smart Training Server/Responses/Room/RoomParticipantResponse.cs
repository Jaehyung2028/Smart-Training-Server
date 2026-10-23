using Smart_Training_Server.Responses.Base;

// 방 접속 참가자 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Room
{
    public sealed class RoomParticipantResponse : BaseResponse
    {
        // 처리 대상 사용자 ID입니다. 요청자의 권한은 서버의 인증 정보로 별도 확인합니다.
        public string UserId { get; set; } = string.Empty;

        // 사용자 닉네임입니다.
        public string UserName { get; set; } = string.Empty;

        // 방 자리 번호입니다. 현재 소속과 함께 해석합니다.
        public int RoomIndex
        {
            get; set;
        }
    }
}
