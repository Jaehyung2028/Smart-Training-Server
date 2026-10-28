namespace Smart_Training_Server.Requests.Room
{
    // 방을 유지하면서 같은 교육과정의 실행 콘텐츠만 바꾸는 방장 요청입니다.
    public sealed class ChangeRoomContentRequest
    {
        public string LobbyId { get; set; } = string.Empty;

        public string RoomId { get; set; } = string.Empty;

        public string ContentId { get; set; } = string.Empty;

        public int ContentNodeId { get; set; }
    }
}
