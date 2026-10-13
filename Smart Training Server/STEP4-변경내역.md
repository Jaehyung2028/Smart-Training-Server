# 4단계: 로비별 방 목록과 상세 조회

## API

- `GET /api/Lobby/{lobbyId}/rooms`: 로비에 속한 방 목록. 생성 시각과 ID 순서로 반환.
- `GET /api/Lobby/{lobbyId}/rooms/{roomId}`: 해당 로비에 속한 방 상세 정보.
- 존재하는 빈 로비는 `200 OK`와 `[]`, 없는 로비/방 또는 다른 로비의 방은 `404 Not Found`.

두 API는 조회용 응답 모델을 사용합니다. 목록의 각 항목과 상세 응답은 동일한 필드를 제공합니다.

```json
{
  "id": "방 ID",
  "lobbyId": "로비 ID",
  "name": "테스트 방",
  "contentId": "sample-training",
  "contentTitle": "테스트 훈련 콘텐츠",
  "maxUsers": 2,
  "connectedUsers": 0,
  "reservedUsers": 1,
  "availableSlots": 1,
  "masterUserId": "방장 사용자 ID",
  "state": 2,
  "createdAt": "2026-10-09T00:00:00Z"
}
```

위 숫자 상태 값은 예시입니다. 실제 상태 값은 기존 Enum/ServerState.cs 정의를 따릅니다.
`connectedUsers`는 실제 접속 확정 인원, `reservedUsers`는 접속 대기 인원입니다.
`availableSlots = maxUsers - connectedUsers - reservedUsers`이며 음수는 0으로 처리합니다.
빈 자리가 있어도 접속 보장을 뜻하지 않습니다. 실제 입장 시 기존 JoinRoom()에서 다시 검사합니다.
사용자 객체 목록, 서버 등록 토큰, 입장 토큰, 콘텐츠 내부 파일 경로는 이 응답에 포함하지 않습니다.

## 변경 파일과 함수 위치

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| _Controllers/RoomController.cs | GetRooms() 추가 | 로비별 방 목록. 없는 로비는 404 |
| 같은 파일 | GetRoom() 추가 | 로비와 방 ID를 함께 확인. 없는 방은 404 |
| Services/Room/RoomService.Query.cs (새 파일) | GetRoomsByLobby() | 해당 로비 방만 읽어 조회용 객체 배열 생성 |
| 같은 파일 | GetRoomByLobby() | 해당 로비에 실제로 속한 방인지 확인 |
| 같은 파일 | ToRoomSummary() | 내부 RoomInfo의 필요한 값만 복사. 인원/예약/빈자리 계산 |
| Responses/Room/RoomSummaryResponse.cs (새 파일) | 응답 속성 | 클라이언트의 방 선택 화면에 필요한 정보 정의 |

RoomService.Query.cs는 partial 파일입니다. 기존 RoomService.cs, RoomService.Admission.cs와 같은 하나의 RoomService 클래스입니다.
방 조회는 사용자 상태 변경과 같은 잠금 안에서 수행합니다. 만료 예약을 정리한 뒤 값을 복사하므로 오래된 예약 인원을 표시하지 않습니다.

## 핵심 코드

RoomController.cs → GetRooms():

```csharp
[HttpGet("/api/Lobby/{lobbyId}/rooms")]
public ActionResult<IReadOnlyList<RoomSummaryResponse>> GetRooms(string lobbyId)
```

RoomController.cs → GetRoom():

```csharp
[HttpGet("/api/Lobby/{lobbyId}/rooms/{roomId}")]
public ActionResult<RoomSummaryResponse> GetRoom(string lobbyId, string roomId)
```

RoomService.Query.cs → ToRoomSummary():

```csharp
ConnectedUsers = room.Users.Count,
ReservedUsers = room.ReservedUsers,
AvailableSlots = Math.Max(0, room.MaxUsers - room.Users.Count - room.ReservedUsers)
```

## 직접 확인

1. 기존 로그인 → 로비 생성 → 로비 입장 → 방 생성 순서로 로비/방 ID를 확보합니다.
2. Swagger에서 새 Get 요청에 로비 ID를 넣습니다. 생성한 방만 조회되는지 확인합니다.
3. 상세 조회에 같은 로비 ID와 방 ID를 넣습니다. 콘텐츠 이름과 인원을 확인합니다.
4. 다른 로비 ID 또는 없는 방 ID로 요청하면 404인지 확인합니다.
5. 방이 없는 실제 로비는 빈 배열이 반환되는지 확인합니다.

## 검증

실제 로컬 HTTP 요청으로 두 URL의 라우팅과 JSON, 빈 목록 200, 없는 로비/방과 잘못된 로비의 404를 확인했습니다.
예약과 실제 접속 인원 구분, 접속 확정 후 인원 갱신, 만료 예약 정리, 토큰/내부 사용자 데이터 제외, 삭제한 방의 404를 검증했습니다.
기존 콘텐츠 생성 및 입장 예약 검사도 다시 실행했습니다. 실제 Unity 클라이언트 연결 테스트는 아직 수행하지 않았습니다.
