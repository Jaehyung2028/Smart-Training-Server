# 통합 점검 설정

점검과 종료 예약을 하나의 운영 상태로 통합했습니다. 현재 동작은 이 문서를 기준으로 합니다.

## 화면에서 사용하기
서버 PC에서 `http://localhost:5229/operations`를 열고 대상 로비의 **점검 설정**을 선택합니다.

| 점검 방식 | 새 로비 입장 | 기존 참가자 |
|---|---|---|
| 입장만 차단 | 차단 | 계속 이용 |
| 예약 종료 | 차단 | 지정 시각에 목록으로 이동, 로비·방 프로그램 종료 |

사유(1~200자)는 필수입니다. 예약 종료는 현재 이후부터 30일 이내의 시각을 브라우저 현지 시각으로 입력합니다. 기존 예약은 **점검 설정 변경**에서 다른 시각으로 바꾸거나 **입장만 차단**으로 전환할 수 있습니다. 입장만 차단으로 전환하면 기존 종료 예약은 제거됩니다.

운영 상태는 `운영 중 → 점검 중 또는 종료 예정 → 점검 중`입니다. 예약 종료가 완료돼도 입장은 계속 차단됩니다. **운영 재개**에서 사유를 입력해 적용하면 입장 차단과 종료 예약을 한 번에 해제합니다. 두 설정을 따로 해제하지 않습니다. 운영 중이라는 표시는 입장 허용을 뜻하며, 실제 프로그램이 켜져 있는지는 서버 상태·PID 열로 확인합니다.

기존 접속·이미 접수된 로비 입장은 지정 시각까지 유지합니다. 종료 시 해당 로비의 모든 방·소속·입장 예약을 정리하고 참가자의 로그인은 유지합니다. 다른 콘텐츠 로비는 영향을 받지 않습니다. 이후 운영을 재개하면 새 입장 요청에 따라 온디맨드로 실행합니다.

## 저장과 이전 설정
`Data/operations-history.json`의 `Modes` 컬렉션 하나에 상태·사유·종료 시각·공통 버전을 저장합니다. 변경 시 즉시 저장을 시도하고 재시작 후 복원합니다. 저장이 꺼졌거나 실패하면 화면에 경고합니다. `SMART_TRAINING_SERVER_PROBE=1`에서는 파일 저장을 사용하지 않습니다.

기존 파일의 분리된 점검·종료 예약은 자동 이전합니다. 종료 예약이 있으면 시각을 유지하고, 종료 완료 또는 기존 점검은 점검 중으로 옮깁니다. 기존 두 설정이 겹쳐 있어도 운영 재개 한 번으로 풀립니다. 이전 프로세스와 사용자 접속을 복원하지는 않습니다. 잘못된 이력 파일 보존·복구 정책은 기존과 같습니다.

종료 검사는 서버 UTC 시각으로 1초마다 수행합니다. Unity의 남은 시간은 클라이언트 시계 기반 안내이므로 실제 화면 복귀 완료 시각에는 차이가 있을 수 있습니다.

## 코드와 API
- `ServerOperationsRegistry.Mode.cs`: `GetOperatingMode()`, `SetOperatingMode()` — 단일 상태와 공통 버전, 중복 요청 처리
- `LobbyShutdownService.cs`: `SetOperatingMode()` 검증·저장, `ExecuteDue()` 예약 실행
- `ServerOperationsRegistry.Persistence.cs`: 신규 `Modes` 저장·복원과 이전 형식 변환
- `OperationsPage.cs`: 점검 방식 선택·설정 변경·운영 재개
- Unity `ApiLobbyScreen.Shutdown.cs`: 통합 상태·사유·종료 안내 표시

`POST /api/operations/lobbies/{lobbyId}/mode`에 `mode`(`running`, `maintenance`, `scheduled`), `reason`, `atUtc`(예약 종료일 때만 UTC 시각), `expectedRevision`을 전달합니다. 다른 설정 버전이면 409로 거절합니다. 서버 PC의 루프백 접속과 `X-Operations-Request: 1` 헤더가 필요하고, 외부 Host·다른 웹사이트 Origin은 거절합니다.

기존 `/maintenance`, `/shutdown` API와 구형 응답 필드는 호환용입니다. 이들 역시 같은 단일 상태와 공통 버전을 사용하며 별도의 점검·종료 플래그를 만들지 않습니다. 이전 `/shutdown` 해제도 전체 운영 재개로 동작합니다.

## 검증
서버 검사 639개 통과. 통합 전환·공통 버전 충돌·기존 겹친 설정 이전·운영 재개·저장 복원을 포함합니다. 실제 Unity 클라이언트 3개와 ASP.NET으로 연동 검사 21개를 통과했습니다. 예약 종료 후 점검 중 전환, 한 번의 운영 재개, 다른 로비 유지, 기존 접속 정리, 재시작 복원을 확인했습니다. Unity 클라이언트와 방 서버 빌드를 통과했습니다.

브라우저에서 입장만 차단 설정 → 예약 종료로 변경 → 운영 재개를 확인했고, 작성 중 새로고침에도 입력을 유지했습니다. 브라우저 오류는 없었습니다.

솔루션 폴더에서 다음과 같이 재검증합니다. 포트 51454와 독립 이력 파일을 사용하며 자신이 시작한 프로세스만 종료합니다.

```powershell
& '.\Tests\Operations\Run-OperatingMode.ps1' -ServerProject '.\Smart Training Server' -ClientExecutable 'C:\Users\yafa7\Desktop\Projects\Smart Training Platform\Builds\Client\Smart Training Client.exe' -EvidenceRoot "$env:TEMP\SmartTrainingOperatingModeChecks"
```
