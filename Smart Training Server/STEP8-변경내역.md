# 8단계: Unity 방 서버 실행 및 종료 관리

## 설정

appsettings.json의 기존 Room 설정에 다음 두 항목을 추가했습니다.

```json
"ServerExecutablePath": "RoomServer/Smart Training Room.exe",
"ServerReadyTimeoutSeconds": 60
```

- 상대 경로는 ASP.NET 실행 파일 폴더(AppContext.BaseDirectory)를 기준으로 해석합니다. 절대 경로도 가능합니다.
- 실행 파일 폴더를 프로세스 작업 폴더로 사용하므로 Unity 빌드의 데이터 폴더를 함께 배치해야 합니다.
- 대기 시간은 1~300초입니다. 실행 경로는 빈 문자열을 허용하지 않습니다.
- 실행 파일 존재 검사는 방 서버를 실제 시작할 때 수행합니다. Unity 파일이 아직 없어도 ASP.NET을 시작하고 로그인/로비/방 생성 및 조회를 할 수 있습니다.
- 서버 시작 대기 중 입장 예약의 내부 기한도 설정된 대기 시간 + 60초로 조정합니다. 접속 정보 반환 후에는 기존 30초 기한을 사용합니다.

## 달라진 동작

1. 최초 방 입장 예약 시 설정 경로의 실행 파일을 확인하고 Unity 프로세스를 창 없이 실행합니다.
2. 같은 방의 실행 중 서버가 있으면 중복 실행을 거절합니다. RoomService는 정상 Running 서버를 재사용합니다.
3. 지정한 시간 안에 Register가 오지 않으면 프로세스를 종료하고 해당 방의 모든 예약을 취소합니다.
4. 1초 주기로 Starting/Running 방의 프로세스 생존을 확인합니다.
5. 예상치 못한 종료 시 READY 대기를 해제하고 예약·실제 참가자·서버 토큰·SignalR 연결 정보를 정리합니다. 사용자 로비 참가 상태는 유지합니다.
6. 방 자체는 Creating 상태로 남겨 다음 입장 요청에서 재시도할 수 있습니다.
7. 이전 서버 토큰의 늦은 등록과 이전 SignalR 연결의 늦은 종료는 새 실행에 영향을 주지 않습니다.
8. ASP.NET 정상 종료 시 관리 중인 자식 프로세스를 종료합니다. Windows Job Object 연결 실패도 이제 실행 실패로 처리합니다.

프로세스 종료는 하위 프로세스까지 요청하고 최대 5초 기다립니다. 실패하면 오류를 숨기지 않으며, 실행 중인 프로세스 추적 정보를 임의로 버리지 않습니다.

## 파일과 함수별 변경

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| Options/RoomOptions.cs | ServerExecutablePath, ServerReadyTimeoutSeconds | 실행 설정 추가 |
| appsettings.json | Room | 기본 실행 경로·대기 시간 명시 |
| Helper/ProcessHelper.cs | LaunchUnityServer() | 경로 해석, 파일 존재 검사, 중복 실행 방지, 작업 폴더 지정, 인자 분리, 창 없이 실행 |
| 같은 파일 | IsProcessRunning() 추가 | 프로세스 생존 여부 확인, 종료 코드 기록 |
| 같은 파일 | KillProcessById(), KillAll() | 하위 프로세스 종료, 5초 제한, 프로세스 핸들 해제, 종료 오류 기록 |
| Helper/ProcessJobHelper.cs | 초기화, AddProcess() | Windows Job Object 생성/설정/연결 오류 검사 및 할당 메모리 해제 |
| Services/Room/IRoomServerProcessManager.cs (신규) | Launch(), IsRunning(), Stop() | RoomService가 프로세스 관리 기능을 호출하는 인터페이스와 기본 구현 |
| Services/Room/RoomService.cs | 생성자 | 프로세스 관리자 주입 및 설정 검증 |
| 같은 파일 | EnsureRoomServer(), LaunchRoomServer() | 설정 경로 사용, 초기화/실행 실패 정리 |
| 같은 파일 | WaitRoomReady() | 설정 대기 시간 사용 및 실패 시 통합 정리 |
| 같은 파일 | DeleteRoom() | 프로세스 관리자 경유 종료 |
| Services/Room/RoomService.Admission.cs | JoinRoom(), ServerDisconnected(), StopUnusedServer() | 예약 대기 기한 설정 연동, SignalR 종료와 프로세스 정리 통합 |
| Services/Room/RoomService.ServerLifecycle.cs (신규) | CheckServerProcesses() | 시작/실행 중 프로세스 감시 |
| 같은 파일 | ResetRoomServer() | 실패·시간 초과·종료의 예약 및 참가 상태 정리 통합 |
| Services/Room/RoomServerMonitorService.cs (신규) | ExecuteAsync() | 1초 주기 감시, 오류 기록 |
| Program.cs | 서비스 및 종료 훅 등록 | 설정 시작 검증, 프로세스 관리자·감시 서비스, 앱 종료 시 KillAll 등록 |

## 핵심 코드

RoomService.cs → LaunchRoomServer():

```csharp
processManager.Launch(
    room.Id,
    room.Port,
    roomOptions.ServerExecutablePath,
    room.ServerToken);
```

RoomService.cs → WaitRoomReady():

```csharp
Task.Delay(TimeSpan.FromSeconds(roomOptions.ServerReadyTimeoutSeconds))
```

RoomService.ServerLifecycle.cs → CheckServerProcesses():

```csharp
if (!processManager.IsRunning(room.Id))
    ResetRoomServer(room, "Unity 프로세스가 종료되었습니다.");
```

## 직접 확인

- Unity 실행 파일이 없는 현재 상태에서 방 생성은 가능하며, 방 입장 시 즉시 실패 응답과 파일 누락 로그를 확인할 수 있습니다.
- 이후 Unity 빌드를 ServerExecutablePath 위치에 배치합니다. Unity 서버는 기존 -Id, -port, -serverToken 인자를 읽고 Netcode 준비 완료 후 Register를 호출해야 합니다.
- Register가 없으면 설정 시간 이후 입장 실패 및 예약 정리가 발생합니다.
- 실제 Unity 연동 후 프로세스를 종료하면 감시 주기에 방 상태가 초기화되고 참가자가 방에서 제거되어야 합니다.

## 검증

실행 실패, 설정 경로 사용, 동시 예약의 준비 시간 초과, 모든 예약 취소, 늦은 READY 거절, READY 전 종료, 실행 중 종료와 실제/예약 인원 정리, 로비 참가 유지, 재시도, 이전 SignalR 종료의 새 실행 보호를 프로세스 대체 객체로 검증했습니다.
실제 Windows 자식 테스트 프로세스를 창 없이 실행하고 종료 감지 및 핸들 해제를 확인했습니다. 실행 파일 누락도 실제 ProcessHelper로 확인했습니다.
기존 콘텐츠·인증·갱신·로그아웃·조회·예약 검증을 함께 실행했습니다.
실제 Unity 빌드와 Netcode/SignalR 통합 테스트는 아직 수행하지 않았습니다. Windows Job Object를 사용하므로 다른 OS 배포에는 별도 구현이 필요합니다.
