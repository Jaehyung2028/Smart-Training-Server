using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Responses.Room;
using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

// 로비별 조회 결과를 값으로 비교해 변경 대상을 모읍니다. 작업 서비스가 SignalR 그룹에 알림을 보냅니다.
namespace Smart_Training_Server.Services.Lobby
{
    public sealed partial class LobbyRoomUpdates
    {
        private readonly LobbyService lobbies;
        private readonly UserService users;
        private readonly RoomService rooms;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public LobbyRoomUpdates(UserService users, RoomService rooms, LobbyService lobbies)
        {
            this.lobbies = lobbies;
            this.users = users;
            this.rooms = rooms;
        }

        private sealed class Subscription
        {
            // 생성에 필요한 값을 전달받아 초기화합니다.
            public Subscription(string lobbyId, string token)
            {
                this.LobbyId = lobbyId;
                this.Token = token;
            }

            // 대상 또는 소속 로비의 ID입니다.
            public string LobbyId
            {
                get;
            }

            // 구독 사용자의 인증 토큰입니다. 현재 유효성과 소속을 확인할 때 사용합니다.
            public string Token
            {
                get;
            }

            // 새 연결은 기존 목록이 같아도 처음 한 번 변경 알림을 받습니다.
            public bool NeedsSnapshot { get; set; } = true;
        }

        // 공개 응답은 불변 record이므로 JSON 문자열을 만들지 않고 값으로 비교할 수 있습니다.
        private sealed record ListingSnapshot(
            IReadOnlyList<LobbySummaryResponse> Lobbies,
            IReadOnlyList<RoomSummaryResponse> Rooms,
            IReadOnlyList<LobbyParticipantResponse> Participants)
        {
            public bool HasSameValues(ListingSnapshot other)
            {
                return Lobbies.SequenceEqual(other.Lobbies) &&
                    Rooms.SequenceEqual(other.Rooms) &&
                    Participants.SequenceEqual(other.Participants);
            }
        }

        // 구독자마다 같은 목록을 보관하지 않고 로비별로 이전 조회 결과 하나만 유지합니다.
        private readonly Dictionary<string, ListingSnapshot> lastSnapshots = new();

        // 현재 SignalR 연결의 구독 대상·토큰·첫 알림 여부를 보관합니다.
        private readonly Dictionary<string, Subscription> subscriptions = new();

        // 구독 변경·그룹 갱신·전송을 순서대로 처리합니다. 비동기 대기에는 lock 대신 SemaphoreSlim을 사용합니다.
        internal SemaphoreSlim GroupGate { get; } = new(1, 1);

        // 실제 SignalR에 등록한 그룹입니다. 구독 정보와 비교해 이전 그룹을 제거할 때 사용합니다.
        private readonly Dictionary<string, string> registeredGroups = new();
        private readonly Dictionary<string, string> registeredRoomGroups = new();

        public static string GetGroupName(string lobbyId)
        {
            return "lobby:" + lobbyId;
        }

        // 호출자는 GroupGate를 보유합니다. 지정한 그룹만 갱신하며, 생략하면 전체를 갱신합니다.
        // 토큰·소속이 끝난 연결은 전송 전에 그룹에서 먼저 뺍니다.
        internal async Task SynchronizeGroupsAsync(IGroupManager groups, string? groupName = null)
        {
            Dictionary<string, string> desired;
            lock (users.MembershipLock)
            {
                desired = subscriptions
                    .Where(pair => groupName == null || GetGroupName(pair.Value.LobbyId) == groupName)
                    .Where(pair => GetSubscribedUser(pair.Value) != null)
                    .ToDictionary(pair => pair.Key, pair => GetGroupName(pair.Value.LobbyId));
            }

            await ApplyGroupsAsync(groups, registeredGroups, desired, groupName);
        }

        public static string GetRoomGroupName(string roomId)
        {
            return "room:" + roomId;
        }

        // GroupGate 안에서 호출합니다. 방의 실제 접속 토큰까지 유효한 연결만 방 그룹에 남깁니다.
        internal async Task SynchronizeRoomGroupsAsync(IGroupManager groups, string? groupName = null)
        {
            var desired = new Dictionary<string, string>();
            lock (users.MembershipLock)
            {
                foreach (var pair in roomChatSubscriptions)
                {
                    var room = pair.Value;
                    string target = GetRoomGroupName(room.RoomId);
                    if (groupName != null && target != groupName)
                    {
                        continue;
                    }

                    if (subscriptions.TryGetValue(pair.Key, out var subscription))
                    {
                        var user = GetSubscribedUser(subscription);
                        if (user != null && rooms.CanUseRoomChat(user.Id, room.RoomId, room.Token))
                        {
                            desired[pair.Key] = target;
                        }
                    }
                }
            }

            await ApplyGroupsAsync(groups, registeredRoomGroups, desired, groupName);
        }

        // 로비와 방이 동일한 그룹 추가·제거 절차를 사용합니다.
        private async Task ApplyGroupsAsync(
            IGroupManager groups,
            Dictionary<string, string> registered,
            Dictionary<string, string> desired,
            string? groupName)
        {
            foreach (var pair in registered.Where(pair => groupName == null || pair.Value == groupName).ToArray())
            {
                if (!desired.TryGetValue(pair.Key, out var group) || group != pair.Value)
                {
                    await groups.RemoveFromGroupAsync(pair.Key, pair.Value);
                    registered.Remove(pair.Key);
                }
            }

            foreach (var pair in desired)
            {
                if (registered.ContainsKey(pair.Key))
                {
                    continue;
                }

                // 추가가 중간에 실패해도 다음 갱신에서 잔여 그룹을 제거할 수 있도록 먼저 기록합니다.
                registered[pair.Key] = pair.Value;
                try
                {
                    await groups.AddToGroupAsync(pair.Key, pair.Value);
                }
                catch
                {
                    Remove(pair.Key);
                    throw;
                }
            }
        }

        // 전송 실패 후 다음 주기에 변경 알림을 다시 만들 수 있게 합니다.
        internal void InvalidateSnapshots()
        {
            lock (users.MembershipLock)
            {
                lastSnapshots.Clear();
            }
        }

        private UserInfo? GetSubscribedUser(Subscription subscription)
        {
            var user = users.FindByAccessToken(subscription.Token);
            return user != null && (subscription.LobbyId.Length == 0 || user.CurrentLobbyId == subscription.LobbyId) ? user : null;
        }

        // 헤더 토큰과 실제 로비 소속을 확인한 연결만 구독 저장소에 넣습니다.
        public bool Subscribe(string connectionId, string lobbyId, string authorization)
        {
            lock (users.MembershipLock)
            {
                if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var token = authorization[7..];
                var user = users.FindByAccessToken(token);
                if (user == null || lobbyId == null || (lobbyId.Length > 0 && user.CurrentLobbyId != lobbyId))
                {
                    return false;
                }

                roomChatSubscriptions.Remove(connectionId);
                subscriptions[connectionId] = new(lobbyId, token);
                return true;
            }
        }

        // SignalR 연결에 해당하는 목록·방 채팅 구독을 지웁니다. 사용자 소속 정리와는 다른 단계입니다.
        public void Remove(string connectionId)
        {
            lock (users.MembershipLock)
            {
                subscriptions.Remove(connectionId);
                roomChatSubscriptions.Remove(connectionId);
            }
        }

        // 구독된 로비만 검사하고 한 주기에서 변경 알림을 모아 보냅니다.
        // 이전 스냅샷과 현재 목록을 비교합니다. 바뀌지 않았다면 알림을 보내지 않고, 인증·소속이 끝난 구독은 제거합니다.
        public IReadOnlyList<(string ConnectionId, string LobbyId, bool Ended)> CollectChanges()
        {
            lock (users.MembershipLock)
            {
                var result = new List<(string, string, bool)>();
                var active = new List<(string Id, Subscription Subscription, string UserId)>();
                foreach (var (id, subscription) in subscriptions.ToArray())
                {
                    var user = GetSubscribedUser(subscription);
                    if (user == null)
                    {
                        Remove(id);
                        result.Add((id, subscription.LobbyId, true));
                    }
                    else
                    {
                        active.Add((id, subscription, user.Id));
                    }
                }

                // 구독자가 없으면 목록 조회 자체를 생략합니다.
                if (active.Count == 0)
                {
                    lastSnapshots.Clear();
                    return result;
                }

                var lobbyRows = lobbies.GetLobbySummaries();
                var activeLobbies = new HashSet<string>();
                foreach (var group in active.GroupBy(item => item.Subscription.LobbyId))
                {
                    string lobbyId = group.Key;
                    activeLobbies.Add(lobbyId);
                    var snapshot = new ListingSnapshot(
                        lobbyRows,
                        lobbyId.Length == 0 ? [] : rooms.GetRoomsByLobby(lobbyId) ?? [],
                        lobbyId.Length == 0 ? [] : rooms.GetLobbyParticipants(lobbyId, group.First().UserId).Participants);
                    bool changed = !lastSnapshots.TryGetValue(lobbyId, out var previous) || !snapshot.HasSameValues(previous);
                    foreach (var item in group)
                    {
                        if (changed || item.Subscription.NeedsSnapshot)
                        {
                            result.Add((item.Id, lobbyId, false));
                        }

                        item.Subscription.NeedsSnapshot = false;
                    }

                    lastSnapshots[lobbyId] = snapshot;
                }

                foreach (var lobbyId in lastSnapshots.Keys.Where(id => !activeLobbies.Contains(id)).ToArray())
                {
                    lastSnapshots.Remove(lobbyId);
                }

                return result;
            }
        }
    }

    public sealed class LobbyRoomUpdatesService : BackgroundService
    {
        private readonly LobbyRoomUpdates updates;
        private readonly IHubContext<LobbyUpdatesHub> hub;
        private readonly ILogger<LobbyRoomUpdatesService> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public LobbyRoomUpdatesService(
            LobbyRoomUpdates updates,
            IHubContext<LobbyUpdatesHub> hub,
            ILogger<LobbyRoomUpdatesService> logger
        )
        {
            this.updates = updates;
            this.hub = hub;
            this.logger = logger;
        }

        // 목록 변경을 주기적으로 수집해 구독한 연결에 변경·구독 종료 알림을 보냅니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1초 간격으로 변경을 확인합니다. 변경 알림을 받은 클라이언트가 최신 목록을 다시 조회하는 방식입니다.
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await updates.GroupGate.WaitAsync(stoppingToken);
                    try
                    {
                        // 만료·퇴장 연결을 제외한 뒤 같은 로비에는 변경 알림을 한 번만 보냅니다.
                        var changes = updates.CollectChanges();
                        await updates.SynchronizeGroupsAsync(hub.Groups);
                        await updates.SynchronizeRoomGroupsAsync(hub.Groups);
                        foreach (var change in changes.Where(change => change.Ended))
                        {
                            await hub.Clients.Client(change.ConnectionId).SendAsync(
                                "SubscriptionEnded", change.LobbyId, cancellationToken: stoppingToken);
                        }

                        foreach (var lobbyId in changes.Where(change => !change.Ended).Select(change => change.LobbyId).Distinct())
                        {
                            await hub.Clients.Group(LobbyRoomUpdates.GetGroupName(lobbyId)).SendAsync(
                                "RoomsChanged", lobbyId, cancellationToken: stoppingToken);
                        }
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    {
                        updates.InvalidateSnapshots();
                        logger.LogWarning(error, "로비 그룹 갱신 또는 알림 전송에 실패했습니다.");
                    }
                    finally
                    {
                        updates.GroupGate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
