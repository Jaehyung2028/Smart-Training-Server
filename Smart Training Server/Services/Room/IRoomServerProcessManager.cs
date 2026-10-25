using Smart_Training_Server.Helper;

// 방 서비스가 프로세스 실행 도구를 교체할 수 있도록 인터페이스를 둡니다. 기본 구현은 ProcessHelper를 호출합니다.
namespace Smart_Training_Server.Services.Room
{
    public interface IRoomServerProcessManager
    {
        // 서버를 실행하고 배정한 포트를 반환합니다. 포트의 배정·해제는 실행 도구가 책임집니다.
        int Launch(string roomId, string executablePath, string serverToken);

        // 해당 방 서버가 살아 있는지 확인하는 인터페이스 계약입니다.
        bool IsRunning(string roomId);

        // 해당 방 서버의 종료를 요청하는 인터페이스 계약입니다.
        void Stop(string roomId);
    }

    public sealed class RoomServerProcessManager : IRoomServerProcessManager
    {
        private readonly string apiBaseUrl;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public RoomServerProcessManager(string apiBaseUrl = "http://127.0.0.1:5229")
        {
            this.apiBaseUrl = apiBaseUrl;
        }

        // 포트 0은 방 서버 포트를 자동 배정하라는 뜻입니다. 배정된 번호를 방 서비스에 반환합니다.
        public int Launch(string roomId, string executablePath, string serverToken)
        {
            return ProcessHelper.LaunchUnityServer(roomId, 0, executablePath, serverToken, apiBaseUrl);
        }

        // 프로세스 관리 도구에 실행 여부를 조회합니다. 훈련 진행 상태를 검사하는 함수는 아닙니다.
        public bool IsRunning(string roomId)
        {
            return ProcessHelper.IsProcessRunning(roomId);
        }

        // 해당 전용 서버 프로세스의 종료를 공통 도구에 요청합니다.
        public void Stop(string roomId)
        {
            ProcessHelper.RequestStop(roomId);
        }
    }
}
