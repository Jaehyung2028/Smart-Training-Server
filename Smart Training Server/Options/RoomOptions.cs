namespace Smart_Training_Server.Options
{
    public class RoomOptions
    {
        // 빈 값은 기존 로컬 IP 자동 선택을 사용합니다. 프로토콜과 포트는 넣지 않습니다.
        public string AdvertisedAddress { get; set; } = string.Empty;
        public string ServerExecutablePath { get; set; } = "RoomServer/Smart Training Room.exe";
        // Unity 방 서버가 접속할 ASP.NET 주소입니다. --urls 변경 시 함께 변경합니다.
        public string ServerApiBaseUrl { get; set; } = "http://127.0.0.1:5229";
        public int ServerReadyTimeoutSeconds { get; set; } = 60;

        public int MinUsers { get; set; }

        public int MaxUsers { get; set; }

        public int NameMinLength { get; set; }

        public int NameMaxLength { get; set; }


        public bool AllowDuplicateName { get; set; }
    }
}

