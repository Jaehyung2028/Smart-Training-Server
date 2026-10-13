# 3단계: ASP.NET 콘텐츠 목록 로딩과 로비 초기화

## 이번 동작

`Contents/catalog.json`을 ASP.NET이 읽습니다. 로비 생성 성공 시 `Lobby.Contents`에 콘텐츠가 이미 등록되어 있으며, Unity 로비 서버를 실행하지 않아도 해당 콘텐츠로 방을 생성할 수 있습니다.
샘플은 콘텐츠 메타데이터입니다. Unity 씬이나 실제 훈련 동작은 포함하지 않습니다.

## 변경 파일과 함수

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| Services/ContentService.cs | 생성자 | 기본 실행 폴더의 Contents/catalog.json 경로 사용. 테스트용 경로와 로거 주입 가능 |
| 같은 파일 | LoadLobbyContents() | 미구현 엑셀 로딩 대신 JSON 배열을 읽어 RootContent를 키로 등록. 파일 누락/빈 목록/잘못된 JSON을 명확히 실패 처리하고 로그 기록 |
| 같은 파일 | ValidateTree() (추가) | 제목, 양수 노드 ID, 트리 안의 ID 중복, Children 배열 검사. RootContent·Depth·ParentId는 트리 위치에 맞게 자동 설정 |
| 같은 파일 | GetExcelFiles(), LoadContentTree() | 미구현 함수 제거. 실제 엑셀 형식이 정해지면 별도 로더로 추가 예정 |
| Services/Lobby/LobbyService.cs | CreateLobby(), CreateLobbyCore() (분리) | 공유 잠금 아래 생성 검사. 콘텐츠를 먼저 로딩하고 성공했을 때만 로비 등록 |
| 같은 파일 | InitializeLobby() | Unity 로비 서버 초기화에서 콘텐츠 로딩 제거 |
| Contents/catalog.json (추가) | sample-training | 테스트용 루트 콘텐츠 1개와 하위 단계 1개 |
| Smart Training Server.csproj | Content 설정 | catalog.json을 빌드 및 게시 결과의 Contents 폴더에 자동 복사 |

## 핵심 변경 코드

`LobbyService.cs` → `CreateLobbyCore()`, `CreateLobbyInfo()` 호출 직후:

```csharp
try
{
    lobby.Contents = contentService.LoadLobbyContents();
}
catch (InvalidDataException ex)
{
    return new CreateLobbyResponse { Success = false, Message = ex.Message };
}
```

`ContentService.cs` → `LoadLobbyContents()`:

```csharp
var roots = JsonSerializer.Deserialize<List<ContentInfo>>(
    File.ReadAllText(catalogPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

## 콘텐츠 파일 작성 방법

```json
[
  {
    "rootContent": "sample-training",
    "id": 1,
    "title": "테스트 훈련 콘텐츠",
    "children": [
      { "id": 2, "title": "기본 훈련 단계", "children": [] }
    ]
  }
]
```

- 루트 RootContent가 방 생성 요청의 ContentId입니다. 대소문자를 구분하지 않고, 앞뒤 공백을 제거합니다.
- 각 트리의 모든 노드에는 서로 다른 양수 Id와 비어 있지 않은 Title이 필요합니다.
- Children은 생략하면 빈 목록으로 취급합니다. null은 허용하지 않습니다.
- Depth, ParentId와 자식의 RootContent는 로더가 계산합니다.
- 각 로비에는 별도 콘텐츠 객체를 제공합니다. 파일 수정은 이후 생성하는 로비부터 반영됩니다. 실행 중인 서버의 기존 로비에 자동 적용되지는 않습니다.
- 개발 중 프로젝트 파일을 수정한 뒤 다시 빌드하면 실행 폴더로 복사됩니다. 배포한 서버는 게시 폴더의 Contents/catalog.json을 사용합니다.
- 잘못된 파일은 로비 생성 실패로 반환하고, 자세한 이유는 서버 로그에 기록합니다.

## 직접 확인할 순서

서버를 실행하고 개발 환경의 Swagger에서 다음 순서로 확인합니다. 기존 생성 주소의 철자 `creat`는 이번 작업에서 변경하지 않았습니다.

1. `POST /api/Auth/login` → UserName 입력, 응답 User.Id 기록.
2. `POST /api/Lobby/creat` → LobbyName 입력. 응답 Lobby.Contents에 sample-training이 있는지 확인하고 Lobby.Id 기록.
3. `POST /api/Lobby/join` → UserId, LobbyId 입력.
4. `POST /api/Room/creat` → 위 ID와 RoomName, ContentId: sample-training, MaxUsers: 2 입력.
5. 성공 응답 Room.Content의 제목과 Children 확인.

방 생성 요청 예시:

```json
{
  "userId": "로그인 응답의 사용자 ID",
  "lobbyId": "로비 생성 응답의 로비 ID",
  "roomName": "테스트 방",
  "contentId": "sample-training",
  "maxUsers": 2
}
```

이번 검증은 로비 생성·입장·방 생성까지입니다. 방 입장 예약 후 실제 Unity 접속에는 Unity 방 서버 구현이 추가로 필요합니다.

## 검증 결과

빌드, 서비스 수준의 로비 생성→입장→콘텐츠 선택→방 생성, 존재하지 않는 콘텐츠 거절, 로비별 객체 분리, 파일 누락·잘못된 JSON·빈 목록·제목 누락·중복 RootContent·노드 ID 오류·null 자식 거절, 파일 수정 후 재시도를 검증했습니다.
기존 예약/접속 확정/퇴장 검사도 다시 통과했습니다. 게시 결과에 Contents/catalog.json이 포함되는지 확인했습니다.
기존 LobbyHub.cs의 null 관련 경고는 남아 있습니다. ContentService.cs의 null 반환 경고는 이번 구현으로 제거되었습니다.
