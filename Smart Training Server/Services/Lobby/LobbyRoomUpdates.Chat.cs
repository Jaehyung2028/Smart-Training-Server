using System.Diagnostics;
using System.Text.Json;

namespace Smart_Training_Server.Services.Lobby;

public sealed partial class LobbyRoomUpdates
{
    private readonly Dictionary<string, long> lastChat = new();

    // Check sender and recipients on every message, before the next cleanup tick.
    public (bool Success, string Message, string? Payload, string[] Recipients) PrepareChat(string connectionId, string? text)
    {
        lock (users.MembershipLock)
        {
            if (!subscriptions.TryGetValue(connectionId, out var subscription) || subscription.LobbyId.Length == 0)
                return (false, "입장한 로비의 연결이 필요합니다.", null, []);
            var user = users.FindByAccessToken(subscription.Token);
            if (user == null || user.CurrentLobbyId != subscription.LobbyId)
                return (false, "로비 연결이 만료되었습니다. 다시 연결해주세요.", null, []);
            text = text?.Trim();
            var error = ValidateChatTextAndRate(user.Id, text);
            if (error != null) return (false, error, null, []);
            var recipients = subscriptions.Where(pair => pair.Value.LobbyId == subscription.LobbyId &&
                users.FindByAccessToken(pair.Value.Token)?.CurrentLobbyId == subscription.LobbyId).Select(pair => pair.Key).ToArray();
            var payload = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), lobbyId = subscription.LobbyId,
                userId = user.Id, nickname = user.Name, text, sentAt = DateTimeOffset.UtcNow });
            return (true, "전송했습니다.", payload, recipients);
        }
    }

    private string? ValidateChatTextAndRate(string userId, string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 200 || text.Any(char.IsControl))
            return "메시지는 줄바꿈 없이 1~200자로 입력해주세요.";
        long now = Stopwatch.GetTimestamp();
        foreach (var entry in lastChat.Where(pair => Stopwatch.GetElapsedTime(pair.Value, now).TotalMinutes >= 1).ToArray())
            lastChat.Remove(entry.Key);
        if (lastChat.TryGetValue(userId, out var previous) && Stopwatch.GetElapsedTime(previous, now).TotalSeconds < 1)
            return "메시지는 1초에 한 번 전송할 수 있습니다.";
        lastChat[userId] = now;
        return null;
    }
}
