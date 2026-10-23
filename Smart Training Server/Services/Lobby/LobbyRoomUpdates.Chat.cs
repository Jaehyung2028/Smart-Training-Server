using System.Diagnostics;
using System.Text.Json;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Responses.Room;

// 로비·방 채팅의 구독 권한, 입력 검증과 수신 대상 구성을 모읍니다. 두 채팅의 권한 조건은 각각 유지합니다.
namespace Smart_Training_Server.Services.Lobby
{
    public sealed partial class LobbyRoomUpdates
    {
        // 사용자별 마지막 채팅 시각입니다. 전송 간격 제한을 적용하는 데 사용합니다.
        private readonly Dictionary<string, long> lastChat = new();

        // 정리 주기를 기다리지 않고 메시지마다 송신자와 수신자의 권한을 확인합니다.
        // 입력과 구독을 확인하고 성공 여부·메시지·전송할 로비 그룹을 반환합니다.
        public (bool Success, string Message, string? Payload, string? GroupName) PrepareChat(string connectionId, string? text)
        {
            lock (users.MembershipLock)
            {
                if (!subscriptions.TryGetValue(connectionId, out var subscription) || subscription.LobbyId.Length == 0)
                {
                    return (false, "입장한 로비의 연결이 필요합니다.", null, null);
                }

                var user = GetSubscribedUser(subscription);
                if (user == null)
                {
                    return (false, "로비 연결이 만료되었습니다. 다시 연결해주세요.", null, null);
                }

                text = text?.Trim() ?? string.Empty;
                var error = ValidateChatTextAndRate(user.Id, text);
                if (error != null)
                {
                    return (false, error, null, null);
                }

                var payload = JsonSerializer.Serialize(new ChatMessageResponse
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LobbyId = subscription.LobbyId,
                    UserId = user.Id,
                    Nickname = user.Name,
                    Text = text,
                    SentAt = DateTimeOffset.UtcNow
                });
                return (true, "전송했습니다.", payload, GetGroupName(subscription.LobbyId));
            }
        }

        // 채팅 길이·내용과 사용자별 전송 간격을 확인합니다. 오류 메시지가 없으면 유효한 입력입니다.
        private string? ValidateChatTextAndRate(string userId, string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 200 || text.Any(char.IsControl))
            {
                return "메시지는 줄바꿈 없이 1~200자로 입력해주세요.";
            }

            long now = Stopwatch.GetTimestamp();
            foreach (var entry in lastChat.Where(pair => Stopwatch.GetElapsedTime(pair.Value, now).TotalMinutes >= 1).ToArray())
            {
                lastChat.Remove(entry.Key);
            }

            if (lastChat.TryGetValue(userId, out var previous) && Stopwatch.GetElapsedTime(previous, now).TotalSeconds < 1)
            {
                return "메시지는 1초에 한 번 전송할 수 있습니다.";
            }

            lastChat[userId] = now;
            return null;
        }

        // API 실행 동안 증가하는 번호입니다. 같은 SignalR 연결로 재입장해도 이전 메시지를 구분합니다.
        private long roomChatSequence;

        private sealed record RoomChatSubscription(string RoomId, string Token);

        // 연결별 방 채팅 구독입니다. 방과 입장 토큰으로 현재 입장을 구분합니다.
        private readonly Dictionary<string, RoomChatSubscription> roomChatSubscriptions = new();

        // 현재 방 소속과 입장 토큰을 검사하여 해당 연결에 방 채팅 수신 권한을 등록합니다.
        public RoomChatSubscriptionResponse SubscribeRoomChat(string connectionId, string roomId, string admissionToken)
        {
            lock (users.MembershipLock)
            {
                if (roomId == null || admissionToken == null)
                {
                    return new RoomChatSubscriptionResponse { Success = false };
                }

                if (!subscriptions.TryGetValue(connectionId, out var subscription))
                {
                    return new RoomChatSubscriptionResponse { Success = false };
                }

                var user = GetSubscribedUser(subscription);
                if (user == null || subscription.LobbyId.Length == 0)
                {
                    return new RoomChatSubscriptionResponse { Success = false };
                }

                if (roomId == "")
                {
                    roomChatSubscriptions.Remove(connectionId);
                    return new RoomChatSubscriptionResponse { Success = true, ChatSequence = roomChatSequence };
                }

                if (!rooms.CanUseRoomChat(user.Id, roomId, admissionToken))
                {
                    return new RoomChatSubscriptionResponse { Success = false };
                }

                roomChatSubscriptions[connectionId] = new(roomId, admissionToken);
                return new RoomChatSubscriptionResponse { Success = true, ChatSequence = roomChatSequence };
            }
        }

        // 방 소속·토큰·구독을 확인하고 같은 방 그룹에 보낼 메시지를 만듭니다.
        public (bool Success, string Message, string? Payload, string? GroupName) PrepareRoomChat(
            string connectionId,
            string roomId,
            string admissionToken,
            string? text
        )
        {
            lock (users.MembershipLock)
            {
                if (!subscriptions.TryGetValue(connectionId, out var subscription) ||
                    !roomChatSubscriptions.TryGetValue(connectionId, out var roomSubscription) ||
                    roomSubscription.RoomId != roomId ||
                    roomSubscription.Token != admissionToken)
                {
                    return (false, "현재 방의 채팅 연결을 확인해주세요.", null, null);
                }

                var user = GetSubscribedUser(subscription);
                if (user == null ||
                    !rooms.CanUseRoomChat(user.Id, roomId, admissionToken))
                {
                    return (false, "방에 접속 중인 참가자만 전송할 수 있습니다.", null, null);
                }

                text = text?.Trim() ?? string.Empty;
                var error = ValidateChatTextAndRate(user.Id, text);
                if (error != null)
                {
                    return (false, error, null, null);
                }

                var payload = JsonSerializer.Serialize(new ChatMessageResponse
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LobbyId = subscription.LobbyId,
                    RoomId = roomId,
                    Sequence = ++roomChatSequence,
                    UserId = user.Id,
                    Nickname = user.Name,
                    Text = text,
                    SentAt = DateTimeOffset.UtcNow
                });
                return (true, "전송했습니다.", payload, GetRoomGroupName(roomId));
            }
        }
    }
}
