# STEP 10 — 로비 요약과 콘텐츠 조회 API

Unity 프로젝트가 생기면 로그인 → 로비 목록 → 로비 상세 → 콘텐츠 선택 → 방 생성 순서로 연결합니다.
모든 조회 요청은 Authorization: Bearer <accessToken> 헤더가 필요합니다.

## API
- GET /api/Lobby: LobbySummaryResponse 배열. 기존 LobbyInfo 배열에서 요약 응답으로 변경했습니다.
- GET /api/Lobby/{lobbyId}: 로비 요약 한 개. 없는 로비는 404.
- GET /api/Lobby/{lobbyId}/contents: 루트 콘텐츠 배열과 Children 트리. 없는 로비는 404, 콘텐츠가 없으면 빈 배열.

로비 요약 필드: id, name, userCount, roomCount, contentCount, createdAt.
userCount는 로비에 가입한 사용자 수이며, 방 접속 사용자도 포함합니다.
roomCount는 현재 로비에 등록된 방 수입니다. contentCount는 루트 콘텐츠 수입니다.
콘텐츠 필드: rootContent, id, parentId, depth, title, children.
방 생성 요청의 ContentId에는 선택한 루트의 rootContent를 사용합니다.
서버 파일 Path, 사용자 객체, 방 객체, 서버 토큰, 접속 예약 토큰은 조회 응답에 포함하지 않습니다.

## 변경 파일과 함수
- _Controllers/LobbyController.cs: GetAll()은 요약 배열 반환; GetLobby(), GetContents() 추가.
- Services/Lobby/LobbyService.cs: 쿼리 파일을 분리하기 위해 partial 선언.
- Services/Lobby/LobbyService.Query.cs: GetLobbySummaries(), GetLobbySummary(), GetContentsByLobby(), ToLobbySummary(), ToContentQuery().
- Responses/Lobby/LobbySummaryResponse.cs: 로비 요약 DTO.
- Responses/Lobby/ContentQueryResponse.cs: 콘텐츠 트리 DTO.

조회 시 가입/퇴장/방 변경과 동일한 MembershipLock을 사용하여 스냅샷을 만듭니다.
콘텐츠 트리도 새 객체로 복사하므로 이미 반환한 응답이 이후 변경에 영향을 받지 않습니다.
기존 GetAll() 서비스 함수는 내부 호환을 위해 유지했습니다.

## 클라이언트 주의사항
GET /api/Lobby의 기존 users, rooms, contents 중첩 필드는 제거되었습니다.
방 목록은 기존 GET /api/Lobby/{lobbyId}/rooms를 사용합니다.
콘텐츠 목록은 새 /contents API를 사용합니다.
아직 Unity 구현은 없으므로 서버 API와 검증을 준비한 단계입니다.

## 검증
.NET 빌드와 기존 전체 검증에 로비 목록/상세/콘텐츠 HTTP 검증을 추가했습니다.
인증, 없는 로비 404, 개수 변화, 빈 로비, 내부 데이터 제외, 독립 스냅샷을 확인합니다.