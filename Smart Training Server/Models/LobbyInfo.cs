using System.Collections.Concurrent;

// 로비 목록 정보와 소속 사용자·방·콘텐츠를 보관합니다. 고정 카탈로그 로비는 비어도 목록에서 유지됩니다.
namespace Smart_Training_Server.Models
{
    // 로비 목록 정보와 소속 사용자·방·콘텐츠를 묶습니다. 별도 Unity 프로세스 없이 ASP.NET이 직접 관리합니다.
    public class LobbyInfo
    {
        // 이 객체의 고유 식별자입니다. 이름이 바뀌어도 ID를 기준으로 찾습니다.
        public string Id { get; set; } = string.Empty;

        // 화면에 표시하는 이름입니다.
        public string Name { get; set; } = string.Empty;

        // 서버에서 기록한 생성 UTC 시각입니다.
        public DateTime CreatedAt
        {
            get; set;
        }

        // 서버에서 마지막 활동을 기록한 시각입니다.
        public DateTime LastActiveTime
        {
            get; set;
        }

        // 로비 입장이 완료된 사용자 목록입니다. 방 참가자는 로비에도 소속됩니다.
        public ConcurrentDictionary<string, UserInfo> Users { get; set; } = new();

        // 이 로비 안에서 생성한 방 목록입니다.
        public ConcurrentDictionary<string, RoomInfo> Rooms { get; set; } = new();

        // 로비에서 제공하는 콘텐츠 목록입니다. 키는 콘텐츠 루트 ID입니다.
        public ConcurrentDictionary<string, ContentInfo> Contents { get; set; } = new();
    }
}
