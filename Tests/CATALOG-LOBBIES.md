# 카탈로그 로비와 온디맨드 실행

사용자가 로비를 생성하는 흐름을 서버 `Contents/catalog.json`에 정의된 고정 로비 목록으로 변경했습니다. 로비 안에서는 기존처럼 방을 생성하고 참가합니다.

## 목록 기준

카탈로그 배열의 최상위 항목 하나가 로비 하나입니다. `rootContent`는 로비 ID, `title`은 표시 이름, `children`은 해당 콘텐츠의 하위 단계입니다. 하위 항목마다 로비가 생기지는 않습니다. 카탈로그 수정 후 ASP.NET 서버를 다시 시작해야 목록에 반영됩니다.

## 실행 기준

- 서버 시작과 로비 목록 조회만으로 Unity 프로그램이 실행되지 않습니다.
- 첫 로비 입장 요청이 해당 로비의 Unity 프로그램을 실행합니다. 프로그램이 `/LobbyServerHub`에 준비 완료를 등록한 뒤 입장을 확정합니다.
- 같은 로비의 동시 입장 요청은 프로그램 하나를 공유합니다. 서로 다른 로비는 서로 다른 프로그램을 실행합니다.
- 확정 사용자와 입장 대기 요청이 모두 없어지면 해당 로비 프로그램을 종료합니다. 카탈로그 로비는 목록에 계속 남습니다.
- 실행 실패·시간 초과·요청 취소는 입장 대기를 정리합니다. 사용자가 남은 로비의 프로그램이 종료되면 재실행합니다.

## 기존 방과의 관계

`ASP.NET → 콘텐츠 로비 → 사용자 생성 방 → Unity Netcode 방 서버` 구조입니다. 이번 로비 프로그램은 등록·연결 유지·실행 수명 관리를 담당합니다. 로비에 별도 Netcode 캐릭터나 훈련 콘텐츠를 추가한 것은 아닙니다. 방의 Netcode 멀티 접속과 방장·준비·진행 상태는 기존 방 서버가 계속 담당합니다.

동일한 서버 실행 파일을 `-lobby` 모드와 기존 방 서버 모드로 구분해서 실행합니다. 로비 모드는 UDP 포트를 사용하지 않습니다. 로비 프로그램과 방 프로그램은 독립적으로 실행·종료됩니다.

## 확인할 함수

- 서버 `LobbyService.InitializeCatalogLobbies()` — 카탈로그를 고정 로비 목록으로 구성
- 서버 `LobbyService.JoinCatalogLobby()` — 입장 대기와 준비 완료 후 입장 확정
- 서버 `LobbyService.StopIdleLobby()` / `MaintainLobbyServers()` — 빈 로비 종료와 필요한 로비 재실행
- 서버 `LobbyServerHub.Register()` — 실행한 프로그램의 비밀 토큰 검증
- Unity `LobbyDedicatedServer.Initialize()` / `Start()` — 로비 모드 실행과 서버 등록
- Unity `ApiLobbyScreen.JoinLobby()` — 준비 중 안내와 입장 요청

## 설정

카탈로그 루트에 `lobbyServerExecutablePath`를 지정할 수 있습니다. 생략하면 `appsettings.json`의 `Lobby.ServerExecutablePath`를 사용합니다. 기본값은 `RoomServer/Smart Training Room.exe`입니다. 상대 경로는 ASP.NET 실행 디렉터리를 기준으로 해석합니다. 준비 제한 시간은 `Lobby.ServerReadyTimeoutSeconds`이며 기본 60초입니다.

## 검증과 수동 확인

서버 검사 512개, 실제 Unity 로비 프로그램 검사 11개, 독립 Unity 클라이언트 3개를 사용한 방 접속 검사 14개를 통과했습니다. 기존 에디터 검사 중 사용자 로비 생성을 전제로 한 과거 시나리오는 새 운영 API에 그대로 적용할 수 없습니다. 이번 구조 검증은 `Tests/MultiClient/Run-CatalogLobbies.ps1`와 갱신한 독립 클라이언트 검사로 수행합니다.

1. ASP.NET 서버와 Unity 에디터를 다시 시작합니다.
2. 로그인 후 카탈로그의 로비가 사용자 없이 표시되고 서버 상태가 꺼짐인지 확인합니다.
3. 로비에 입장해서 서버 준비 후 방 생성·참가가 가능한지 확인합니다.
4. 방을 먼저 나가고 마지막 사용자가 로비에서도 나가면 로비 프로그램이 종료되는지 확인합니다. 목록은 유지되어야 합니다.
5. 여러 최상위 콘텐츠를 정의했다면 서로 다른 로비에 입장할 때 로비 프로그램도 각각 실행되는지 확인합니다.

현재 실제 카탈로그에는 최상위 콘텐츠가 하나이므로 기본 로비도 하나입니다. 두 로비 동시 실행 검사는 실제 콘텐츠 파일을 바꾸지 않고 테스트 전용 카탈로그로 수행했습니다.

PowerShell에서 검사할 때 두 경로를 실제 환경에 맞춰 지정합니다.

```powershell
& '.\Tests\MultiClient\Run-CatalogLobbies.ps1' -ServerProject 'C:\Users\yafa7\Desktop\Projects\Smart Training Server\Smart Training Server\Smart Training Server' -EvidenceRoot "$env:TEMP\SmartTrainingCatalogChecks"
```
