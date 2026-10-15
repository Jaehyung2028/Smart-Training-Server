현재 운영 설정은 [통합 점검 설정](OPERATING-MODE.md)을 기준으로 합니다. 점검 방식 선택과 운영 재개를 한 화면에서 처리합니다.

# 서버 운영 상태 조회

서버가 준비되면 Windows의 기본 브라우저에서 `http://localhost:5229/operations`를 한 번 자동으로 엽니다. 서버를 실행한 PC에서 이 주소를 직접 열어도 됩니다. 포트를 변경했다면 `appsettings.json`의 `Server.Port`를 사용합니다. 같은 정보를 JSON으로 확인하는 주소는 `GET /api/operations`입니다.

## 표시 정보

- 로비·방 이름과 ID, 소속 로비, 현재 서버 상태
- 실제 Unity 프로그램의 실행 여부와 PID
- 로비의 확정 소속 인원·입장 대기 인원·방 개수
- 방의 Netcode 확인 접속 인원·입장 예약 인원·정원·훈련 진행 여부
- 마지막 실행 시도 시각, 종료 시각, 마지막 실패 시각과 사유
- 최근 실행 준비·준비 완료·정상 종료·실패 기록

화면은 5초마다 자동 갱신하고 `지금 새로고침` 버튼도 제공합니다. 화면이 좁으면 각 표를 가로로 스크롤할 수 있습니다. 시각은 브라우저의 시간대로 표시하며 API 시각은 UTC입니다. 조회 실패 시 이전 정보가 최신 상태가 아닐 수 있음을 알립니다.

서버 상태와 실제 프로그램 실행 여부를 따로 표시합니다. 예를 들어 프로그램이 비정상 종료된 직후에는 상태 감지가 진행되는 동안 두 값이 잠시 다를 수 있습니다. 로비 인원에는 방에 참가 중인 사용자도 포함합니다.

## 접근 범위

이 화면과 API는 서버 PC의 루프백 주소에서만 열 수 있습니다. `localhost`, `127.0.0.1`, `::1`을 사용합니다. 다른 PC에서 보내는 요청은 일반 사용자 인증 토큰이 있어도 허용하지 않습니다. 전달 헤더를 실제 접속 주소로 신뢰하지 않으며, 외부 Host 및 다른 웹사이트 Origin 요청도 거절합니다.

별도 관리자 계정이나 원격 관리 권한을 추가한 것은 아닙니다. 서버 PC에 접근할 수 있는 사용자를 위한 로컬 관리 화면입니다. 로비별 점검 설정·해제와 변경 사유 입력을 지원합니다. 기존 참가자는 유지하고 새 로비 입장만 차단합니다. 상세 동작과 검증은 `MAINTENANCE.md`를 참고해주세요. 조회 응답에는 사용자 이름·ID, 인증 토큰, 입장 예약 토큰, 실행 파일 경로를 포함하지 않습니다.

## 기록 범위

이력은 메모리에서 조회하며 기본적으로 서버 콘텐츠 루트의 `Data/operations-history.json`에도 보관합니다. 2초마다 변경분을 저장하고 정상 종료 시 마지막 기록을 저장합니다. ASP.NET 서버를 재시작하면 파일에서 이력을 불러옵니다. 강제 종료 직전의 최대 약 2초 기록은 저장되지 않을 수 있습니다. 최근 이벤트는 200건, 종료되거나 실패한 프로그램의 최근 상태는 최대 512개를 보관합니다. 실행 중인 프로그램의 기록은 메모리 한도 때문에 제거하지 않습니다.

이전 실행이 정상 종료되지 않았다면 당시 실행 중이던 프로그램은 현재 실행 상태가 아닌 `이전 실행 중단` 이력으로 바꿉니다. 실제 프로그램이나 사용자 접속을 자동 복원하지 않습니다. 저장 오류가 나면 화면에 안내하고 메모리 기록을 유지하며 재시도합니다. 손상된 파일은 `.invalid-<시각값>` 복사본을 남기고 새 기록을 시작합니다.

자동 열기는 `Operations.OpenBrowserOnStart`, 파일 저장은 `Operations.PersistHistory`, 저장 경로는 `Operations.HistoryPath`로 설정합니다. `SMART_TRAINING_SERVER_PROBE=1`인 개발용 클라이언트 검사 모드에서는 애플리케이션의 브라우저 자동 열기와 파일 저장을 사용하지 않아 검사 실행이 실제 운영 기록에 섞이지 않습니다. Windows의 비대화형 서비스나 다른 운영체제에서는 화면 주소를 직접 열어주세요. Visual Studio의 기존 Swagger 자동 열기는 유지합니다.

방이 삭제되어 현재 방 표에서 사라진 뒤에도 최근 이벤트와 API의 `recentProcesses`에서 해당 방의 종료·실패 기록을 확인할 수 있습니다. 재실행에 성공해도 마지막 실패 이력은 유지합니다. 정상 종료와 입장 취소는 실패로 기록하지 않으며 반복 정리는 중복 종료 이벤트를 만들지 않습니다.

서버 콘솔에는 로비와 방 모두 다음 형식으로 실행 수명 기록을 남깁니다.

```text
[Operations] kind=room lobbyId=sample-training id=<방 ID> event=starting at=<UTC 시각> reason=
```

`kind`, `lobbyId`, `id`로 로비·방을 식별할 수 있습니다. 기존 상세 오류 로그는 유지합니다.

## 주요 파일과 함수

- `_Controllers/OperationsController.cs`: `GetStatus()`, `Page()`, `IsLocalRequest()`
- `Services/Operations/ServerOperationsService.cs`: `GetSnapshot()` — 현재 상태와 기록을 읽기 전용 DTO로 복사
- `Services/Operations/ServerOperationsRegistry.cs`: `Record()`, `Snapshot()` — 이력 저장·제한·중복 종료 제거
- `Services/Operations/OperationsPage.cs`: `Html` — 관리 화면과 자동 갱신
- `Services/Room/RoomService.Operations.cs`: `GetOperationsRooms()` — 예약 만료 등을 실행하지 않는 조회
- `Helper/ProcessHelper.cs`: `GetProcessStatus()` — 프로세스 잠금 안에서 PID·실행 여부 확인

기존 로비·방 서비스의 실행, 준비 완료, 종료, 실패 지점에 기록을 연결했습니다. 관리 화면의 조회로 프로그램을 실행하거나 접속 상태를 변경하지 않습니다. Unity 로비 목록에는 점검 상태·사유를 표시하고 새 입장 버튼을 제한합니다.

## 검증

- 서버 검사 639개 통과: 현재 상태, 실패 후 재실행, 삭제 후 이력, 조회 독립성, 비밀정보 제외, 접근 제한, 이력 제한 및 중복 종료 포함
- 실제 Unity 참가자와 프로세스 검사 15개 통과: 실제 PID·접속·예약 수, 방·로비 강제 종료와 재시작, 삭제 후 이력 확인
- 브라우저에서 로비 3개·방 1개 표시, 수동 새로고침, 5초 자동 갱신, 콘솔 오류 없음 확인

서버 솔루션 폴더에서 재실행할 수 있습니다. 테스트 서버는 별도 포트 51440을 사용하고 자신이 시작한 프로세스만 종료합니다.

```powershell
& '.\Tests\Operations\Run-Operations.ps1' -ServerProject '.\Smart Training Server' -ClientExecutable 'C:\Users\yafa7\Desktop\Projects\Smart Training Platform\Builds\Client\Smart Training Client.exe' -EvidenceRoot "$env:TEMP\SmartTrainingOperationsChecks"
```

실행 전 ASP.NET 프로젝트를 빌드해야 합니다. 실제 카탈로그의 `sample-training` 로비와 현재 Unity 개발 빌드를 사용합니다. 통과 결과는 `Tests/Operations/RESULTS.txt`에 기록했습니다.
