using System.Text.Json;

namespace Smart_Training_Server.Services.Lobby;

public sealed partial class LobbyRoomUpdates
{
    private sealed record RoomChatSubscription(string RoomId, string Token, string BindingId);
    private readonly Dictionary<string, RoomChatSubscription> roomChatSubscriptions = new();

    public bool SubscribeRoomChat(string connectionId, string roomId, string admissionToken, string bindingId)
    {
        lock (users.MembershipLock)
        {
            if (roomId == null || admissionToken == null || bindingId == null) return false;
            if (!subscriptions.TryGetValue(connectionId, out var subscription)) return false;
            var user = users.FindByAccessToken(subscription.Token);
            if (user == null || subscription.LobbyId.Length == 0 || user.CurrentLobbyId != subscription.LobbyId) return false;
            if (roomId == "") { roomChatSubscriptions.Remove(connectionId); return true; }
            if (!Guid.TryParseExact(bindingId, "N", out _) || !rooms.CanUseRoomChat(user.Id, roomId, admissionToken)) return false;
            roomChatSubscriptions[connectionId] = new(roomId, admissionToken, bindingId);
            return true;
        }
    }

    public (bool Success, string Message, string? Payload, IReadOnlyList<(string ConnectionId, string BindingId)> Recipients)
        PrepareRoomChat(string connectionId, string roomId, string admissionToken, string bindingId, string? text)
    {
        lock (users.MembershipLock)
        {
            if (!subscriptions.TryGetValue(connectionId, out var subscription) ||
                !roomChatSubscriptions.TryGetValue(connectionId, out var binding) || binding.RoomId != roomId ||
                binding.Token != admissionToken || binding.BindingId != bindingId)
                return (false, "현재 방의 채팅 연결을 확인해주세요.", null, []);
            var user = users.FindByAccessToken(subscription.Token);
            if (user == null || user.CurrentLobbyId != subscription.LobbyId || !rooms.CanUseRoomChat(user.Id, roomId, admissionToken))
                return (false, "방에 접속 중인 참가자만 전송할 수 있습니다.", null, []);
            text = text?.Trim();
            var error = ValidateChatTextAndRate(user.Id, text);
            if (error != null) return (false, error, null, []);
            var recipients = new List<(string, string)>();
            foreach (var (id, candidate) in roomChatSubscriptions)
            {
                if (candidate.RoomId != roomId || !subscriptions.TryGetValue(id, out var recipient)) continue;
                var recipientUser = users.FindByAccessToken(recipient.Token);
                if (recipientUser?.CurrentLobbyId == subscription.LobbyId && recipient.LobbyId == subscription.LobbyId &&
                    rooms.CanUseRoomChat(recipientUser.Id, roomId, candidate.Token)) recipients.Add((id, candidate.BindingId));
            }
            var payload = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), lobbyId = subscription.LobbyId, roomId,
                userId = user.Id, nickname = user.Name, text, sentAt = DateTimeOffset.UtcNow });
            return (true, "전송했습니다.", payload, recipients);
        }
    }
}
