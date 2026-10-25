// appsettings의 Room 설정을 받습니다. 방 서버 실행 파일·주소·준비 시간과 방 이름·정원 규칙을 결정합니다.
namespace Smart_Training_Server.Options
{
    public class RoomOptions
    {
        // 빈 값은 기존 로컬 IP 자동 선택을 사용합니다. 프로토콜과 포트는 넣지 않습니다.
        public string AdvertisedAddress { get; set; } = string.Empty;

        // ASP.NET이 실행할 Unity 방 서버 프로그램 경로입니다.
        public string ServerExecutablePath { get; set; } = "RoomServer/Smart Training Room.exe";

        // Unity 방 서버가 접속할 ASP.NET 주소입니다. --urls 변경 시 함께 변경합니다.
        public string ServerApiBaseUrl { get; set; } = "http://127.0.0.1:5229";

        // 프로세스 시작 후 준비 등록을 기다리는 최대 시간(초)입니다.
        public int ServerReadyTimeoutSeconds { get; set; } = 60;

        // 방 생성·변경 시 허용하는 정원의 최솟값입니다.
        public int MinUsers { get; set; }

        // 방 생성·변경 시 허용하는 정원의 최댓값입니다.
        public int MaxUsers { get; set; }

        // 방 이름의 최소 글자 수입니다.
        public int NameMinLength { get; set; }

        // 방 이름의 최대 글자 수입니다.
        public int NameMaxLength { get; set; }

        // 같은 로비 안에서 같은 방 이름을 허용할지 결정합니다.
        public bool AllowDuplicateName { get; set; }
    }
}
