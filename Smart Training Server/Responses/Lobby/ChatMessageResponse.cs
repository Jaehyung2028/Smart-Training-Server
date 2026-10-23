using System.Text.Json.Serialization;

// 로비·방 채팅이 클라이언트에 전달하는 메시지입니다. roomId는 방 채팅에만 포함합니다.
namespace Smart_Training_Server.Responses.Lobby
{
    public sealed class ChatMessageResponse
    {
        // 채팅은 별도로 JSON 문자열을 만들기 때문에 직렬화 기본 설정에 상관없이 기존 이름을 유지합니다.
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("lobbyId")]
        public string LobbyId { get; set; } = string.Empty;

        [JsonPropertyName("roomId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RoomId
        {
            get; set;
        }

        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("nickname")]
        public string Nickname { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        // 방 채팅에만 사용하는 서버 메시지 번호입니다. 로비 채팅 JSON에는 포함하지 않습니다.
        [JsonPropertyName("sequence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? Sequence
        {
            get; set;
        }

        [JsonPropertyName("sentAt")]
        public DateTimeOffset SentAt
        {
            get; set;
        }
    }
}
