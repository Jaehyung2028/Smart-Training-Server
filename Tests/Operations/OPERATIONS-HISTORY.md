# 운영 화면 자동 열기와 이력 파일 보관

## 서버 실행 시 운영 페이지 열기

서버가 정상적으로 바인딩을 마치고 `ApplicationStarted`를 알린 뒤 기본 브라우저에서 운영 페이지를 한 번 엽니다. 기본 주소는 `http://localhost:5229/operations`이며 `Server.Port` 설정을 따릅니다. 준비되기 전에 화면을 열거나 같은 서버 실행에서 여러 번 여는 것을 방지합니다.

Visual Studio의 기존 Swagger 자동 열기는 유지했습니다. IDE 실행에서는 기존 Swagger와 운영 페이지가 각각 열립니다. 실행 파일로 직접 시작할 때도 애플리케이션이 운영 페이지를 엽니다. 운영 페이지를 여는 처리는 같은 실행에서 한 번만 수행합니다.

`Operations.OpenBrowserOnStart=false`로 자동 열기를 끌 수 있습니다. 브라우저 실행 실패는 로그로 안내하고 서버 실행은 유지합니다. 기본 브라우저 자동 열기는 대화형 Windows 환경에서 사용합니다.

## 기록 보관

기본 설정은 다음과 같습니다.

```json
"Operations": {
  "OpenBrowserOnStart": true,
  "PersistHistory": true,
  "HistoryPath": "Data/operations-history.json"
}
```

상대 저장 경로는 ASP.NET 콘텐츠 루트를 기준으로 해석합니다. 기본 개발 실행에서는 ASP.NET 프로젝트의 `Data/operations-history.json`입니다. 기본 기록 파일과 임시 쓰기·손상 복사본은 Git 및 빌드·게시 콘텐츠에서 제외했습니다.

변경된 기록을 2초마다 저장합니다. 임시 파일을 완성한 뒤 본 파일을 교체하므로 쓰는 도중 종료되면 이전 유효 파일을 사용할 수 있습니다. 정상 종료 시 활성 기록을 정상 종료로 처리하고 마지막 기록을 저장합니다. 종료 이후의 늦은 연결 해제 콜백은 정상 종료를 실패로 바꾸지 않습니다.

재시작하면 최근 이력, 실행 횟수, 마지막 실패를 불러옵니다. 저장된 이력에서 실행 중이던 항목은 이전 ASP.NET 실행이 중단된 기록으로 처리합니다. 새 서버의 로비 상태·실제 PID·사용자 수와 구분하며, 이전 프로그램이나 로그인 상태를 자동 복구하지 않습니다.

파일 저장 실패는 관리 화면에 표시합니다. 메모리 기록을 유지하고 다음 주기에 저장을 재시도합니다. JSON이 손상되었거나 형식이 맞지 않으면 원본의 `.invalid-<시각값>` 복사본을 보존하고 서버는 새 기록으로 실행합니다. 읽을 파일은 최대 8 MB이며 최근 이벤트·종료 기록 제한은 기존 방식과 같습니다.

강제 종료 직전 최대 약 2초의 최신 이벤트는 유실될 수 있습니다. 이 기능은 최근 운영 이력 보관이며 모든 로그를 무제한 저장하는 방식은 아닙니다. 사용자 ID·인증 토큰·입장 토큰을 저장하지 않습니다.

## 변경 함수

- `OperationsBrowserLauncher.StartAsync()` / `OpenOnce()` — 서버 준비 후 1회 자동 열기
- `OperationsBrowserOpener.Open()` — 기본 브라우저 실행
- `ServerOperationsRegistry.RestoreHistory()` / `FlushHistory()` — 파일 복구와 변경분 저장
- `ServerOperationsRegistry.CompleteSession()` — 정상 종료 기록과 마지막 저장
- `OperationsHistoryWriter.ExecuteAsync()` / `StopAsync()` — 주기 저장과 종료 처리
- `ServerOperationsService.GetSnapshot()` — 화면에 저장 사용 여부·최근 저장 시각·저장 오류 전달

## 검증

서버 검사 559개와 실제 API·Unity 로비 프로그램 재시작 검사 12개를 통과했습니다. 자동 열기의 시작 시점·1회 실행·포트 적용·설정 해제·실패 허용을 테스트용 브라우저 실행기로 검증했고, 저장·복구·부분 쓰기·강제 종료·정상 종료·손상 파일·IO 실패와 재시도를 확인했습니다.

서버 솔루션 폴더에서 실행할 수 있습니다.

```powershell
& '.\Tests\Operations\Run-HistoryRestart.ps1' -ServerProject '.\Smart Training Server' -EvidenceRoot "$env:TEMP\SmartTrainingHistoryChecks"
```

검사는 별도 포트 51445와 증거 폴더의 이력 파일을 사용합니다. 실제 운영 이력과 원본 카탈로그는 수정하지 않습니다. 반복 실행 중 브라우저 창이 열리지 않도록 해당 테스트에서 자동 열기는 껐습니다.

## 수동 확인

1. ASP.NET 서버를 실행하면 준비 완료 후 운영 페이지가 자동으로 열리는지 확인합니다.
2. 로비 입장·퇴장 후 화면의 파일 저장 사용·마지막 저장 시각을 확인합니다.
3. 서버를 종료하고 다시 실행해서 최근 기록과 마지막 실패가 유지되는지 확인합니다.
4. 새 서버의 실제 실행 상태와 인원은 초기 상태이고, 입장 요청 시 로비 프로그램이 실행되는지 확인합니다.
