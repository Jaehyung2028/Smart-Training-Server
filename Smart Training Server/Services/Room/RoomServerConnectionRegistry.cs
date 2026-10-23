using System.Collections.Concurrent;

// SignalR 연결 ID → 방 ID 대응표입니다. 서버 비밀값 검증과 최종 권한 판단은 RoomService가 담당합니다.
namespace Smart_Training_Server.Services.Room
{
    // 전용 서버의 SignalR 연결별로 등록한 방을 기록합니다.
    // 서버 비밀값과 현재 연결의 권한은 RoomService에서 검증합니다.
    public sealed class RoomServerConnectionRegistry
    {
        // SignalR 연결 ID에서 등록된 방 ID를 찾기 위한 대응표입니다.
        private readonly ConcurrentDictionary<string, string> roomsByConnection = new();

        // 현재 SignalR 연결 ID에 방 ID를 연결합니다. 이 표만으로 서버 비밀값 검증을 대신하지는 않습니다.
        public void AddConnection(string connectionId, string roomId)
        {
            roomsByConnection[connectionId] = roomId;
        }

        // 연결 ID로 등록된 방을 찾습니다. 없으면 false이므로 Hub가 먼저 등록을 요구합니다.
        public bool TryGetRoomId(string connectionId, out string? roomId)
        {
            return roomsByConnection.TryGetValue(connectionId, out roomId);
        }

        // 종료된 연결의 대응표를 지우면서 어느 방이었는지도 반환합니다.
        public bool RemoveConnection(string connectionId, out string? roomId)
        {
            return roomsByConnection.TryRemove(connectionId, out roomId);
        }
    }
}
