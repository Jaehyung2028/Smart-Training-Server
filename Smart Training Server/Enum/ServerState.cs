// Unity 방 서버 프로세스의 생명주기 상태를 정의합니다. 훈련 진행 여부인 RoomInfo.IsRunning과 구분합니다.
namespace Smart_Training_Server.Enum
{
    public enum ServerState
    {
        // 호출 흐름에서는 Creating을 실행 대기로도 사용합니다. Ready·Stopped·Failed 등은 선언된 상태이며 모든 경로에서 전부 사용되는 것은 아닙니다.
        Creating, // 생성 중
        Starting, // 프로세스 실행 중
        Ready, // 접속 가능
        Running, // 사용 중
        Stopping, // 종료 중
        Stopped, // 종료됨
        Failed // 실행 실패
    }
}
