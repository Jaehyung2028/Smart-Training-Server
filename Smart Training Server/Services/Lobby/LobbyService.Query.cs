using Smart_Training_Server.Models;
using Smart_Training_Server.Responses.Lobby;

// 내부 로비 정보를 공개용 응답으로 변환합니다. 서버 비밀값과 실행 파일 경로는 이 목록 응답에 포함하지 않습니다.
namespace Smart_Training_Server.Services.Lobby
{
    public partial class LobbyService
    {
        // 전체 로비를 안정된 순서로 정렬해 목록용 응답으로 반환합니다.
        public IReadOnlyList<LobbySummaryResponse> GetLobbySummaries()
        {
            lock (userService.MembershipLock)
            {
                return lobbies.Values.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).Select(ToLobbySummary).ToArray();
            }
        }


        // 최상위 콘텐츠 하나가 교육과정 하나입니다. 로그인 전에는 필요한 공개 정보만 전달합니다.
        public IReadOnlyList<CourseSummaryResponse> GetCourses()
        {
            return GetLobbySummaries().Select(lobby => new CourseSummaryResponse { Id = lobby.Id, Name = lobby.Name, CanEnter = lobby.OperatingMode == "running" && !lobby.IsMaintenance && lobby.ShutdownState == "none", UnavailableReason = lobby.OperatingReason }).ToArray();
        }


        // 로비 하나를 요약 응답으로 변환합니다. 존재하지 않으면 null입니다.
        public LobbySummaryResponse? GetLobbySummary(string lobbyId)
        {
            lock (userService.MembershipLock)
            {
                return lobbies.TryGetValue(lobbyId, out var lobby) ? ToLobbySummary(lobby) : null;
            }
        }


        // 지정 로비의 콘텐츠를 공개용 트리 응답으로 변환합니다. 실행 경로 같은 내부 정보는 응답에서 제외합니다.
        public IReadOnlyList<ContentQueryResponse>? GetContentsByLobby(string lobbyId)
        {
            lock (userService.MembershipLock)
            {
                return lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Contents.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => ToContentQuery(x.Value)).ToArray() : null;
            }
        }


        private LobbySummaryResponse ToLobbySummary(LobbyInfo lobby)
        {
            // 같은 로비의 운영 정보를 속성마다 다시 조회하지 않고 한 번씩 읽어 응답에 담습니다.
            var mode = operations.GetOperatingMode(lobby.Id);
            var shutdown = operations.GetShutdown(lobby.Id);
            var maintenance = operations.GetMaintenance(lobby.Id);
            return new LobbySummaryResponse
            {
                Id = lobby.Id,
                Name = lobby.Name,
                ContentId = lobby.Id,
                OperatingMode = mode.Mode,
                OperatingReason = mode.Reason,
                OperatingRevision = mode.Revision,
                ShutdownAtUtc = shutdown.AtUtc,
                ShutdownReason = shutdown.Reason,
                ShutdownState = shutdown.State,
                ShutdownRevision = shutdown.Revision,
                IsMaintenance = maintenance.Enabled,
                MaintenanceReason = maintenance.Reason,
                MaintenanceChangedAtUtc = maintenance.ChangedAtUtc,
                MaintenanceRevision = maintenance.Revision,
                UserCount = lobby.Users.Count,
                RoomCount = lobby.Rooms.Values.Count(room => room.IsListed),
                ContentCount = lobby.Contents.Count,
                CreatedAt = lobby.CreatedAt
            };
        }


        private static ContentQueryResponse ToContentQuery(ContentInfo content)
        {
            return new()
            {
                RootContent = content.RootContent,
                Id = content.Id,
                ParentId = content.ParentId,
                Depth = content.Depth,
                Title = content.Title,
                ContentType = content.ContentType,
                AssetId = content.AssetId,
                Children = content.Children.Select(ToContentQuery).ToArray()
            };
        }
    }
}
