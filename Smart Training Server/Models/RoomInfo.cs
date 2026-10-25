using System.Collections.Concurrent;
using Smart_Training_Server.Enum;

// 방의 관리 상태를 보관합니다. 예약·접속 인원, 방장, 설정과 Unity 프로세스 상태를 서비스가 함께 갱신합니다.
namespace Smart_Training_Server.Models
{
    // ASP.NET이 관리하는 방 상태입니다. 실제 Unity 프로세스와 플레이어 연결 자체를 담는 객체는 아닙니다.
    public class RoomInfo
    {
        [System.Text.Json.Serialization.JsonIgnore]
        // 비밀번호 해시에 사용하는 난수입니다. JsonIgnore가 있으므로 API 응답 JSON에 포함하지 않습니다.
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

        [System.Text.Json.Serialization.JsonIgnore]
        // 비밀번호 원문 대신 저장하는 검증용 해시입니다. 목록에 공개하지 않습니다.
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();

        // 해시 유무로 비밀번호 방인지 판단합니다. 클라이언트에는 이 여부를 알려줄 수 있습니다.
        public bool HasPassword => PasswordHash.Length > 0;

        [System.Text.Json.Serialization.JsonIgnore]
        // 종료 처리 중인 방에 새로운 작업이 들어오는 것을 막기 위한 내부 상태입니다.
        public bool IsClosing { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        // 실행한 Unity 방 서버를 등록할 때 확인하는 비밀값입니다. 사용자 인증 토큰과 다릅니다.
        public string ServerToken { get; set; } = string.Empty;

        // 접속 준비 중인 예약 인원입니다. 현재 접속 인원과 합산하여 정원을 검사합니다.
        public int ReservedUsers { get; set; }

        // 방장이 새 입장을 막았는지 나타냅니다.
        public bool IsEntryLocked { get; set; }

        // 훈련 진행 여부입니다. 아래 State의 프로세스 준비 상태와 다른 개념입니다.
        public bool IsRunning { get; set; }

        // 진행 상태 변경 버전입니다. 오래된 상태를 기준으로 한 요청을 거절할 때 사용합니다.
        public long PhaseRevision { get; set; }

        // 이 방에서 사용할 콘텐츠 정보입니다.
        public ContentInfo Content { get; set; } = new();

        // 방을 구분하는 고유 ID입니다.
        public string Id { get; set; } = string.Empty;

        // 이 방이 속한 로비 ID입니다. 다른 로비의 방을 잘못 조작하지 않도록 함께 확인합니다.
        public string LobbyId { get; set; } = string.Empty;

        // 방 목록과 방 내부에 표시할 이름입니다.
        public string Name { get; set; } = string.Empty;

        // Unity 방 서버의 생성·시작·실행·종료 상태입니다.
        public ServerState State { get; set; }

        // 클라이언트에게 안내하는 Unity 방 서버 주소입니다. ASP.NET API 주소와 구분합니다.
        public string IpAddress { get; set; } = string.Empty;

        // 해당 Unity 방 서버가 사용할 통신 포트입니다.
        public int Port { get; set; }

        // 방 정보를 생성한 UTC 시각입니다.
        public DateTime CreatedAt { get; set; }

        // 마지막 활동 시각입니다. 서버 쪽에서 갱신합니다.
        public DateTime LastActiveTime { get; set; }

        // 예약 인원까지 포함하여 적용할 최대 정원입니다.
        public int MaxUsers { get; set; }

        // 현재 방장 사용자 ID입니다. 방장 권한은 이 값과 실제 접속 상태를 함께 검사합니다.
        public string MasterUserId { get; set; } = string.Empty;

        // 실제 접속이 확정된 참가자 목록입니다. 키는 사용자 ID이고 예약자는 별도 관리합니다.
        public ConcurrentDictionary<string, UserInfo> Users { get; set; } = new();
    }
}
