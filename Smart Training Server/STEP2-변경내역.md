# 2단계: 방 입장 예약과 실제 접속 확정

## 달라진 동작

이전에는 `POST /api/Room/join` 성공 시 `room.Users`와 `user.CurrentRoomId`를 즉시 갱신했습니다.
이제는 자리를 예약하고 Unity 접속 정보를 반환합니다. 실제 접속 완료를 Unity 방 서버가 확인해야 방 인원에 등록됩니다.

1. 클라이언트가 기존 `POST /api/Room/join`을 호출합니다.
2. 응답의 `room.ipAddress`, `room.port`, `admissionToken`, `expiresAtUtc`를 받습니다.
3. Unity 클라이언트는 사용자 ID와 입장 토큰을 Netcode 접속 데이터에 넣습니다.
4. Unity 방 서버는 접속 승인 시 `RoomHub.ValidateConnection(userId, admissionToken)`을 호출합니다. 실패하면 연결을 거절합니다.
5. 실제 접속 완료 후 `RoomHub.ConfirmConnection(userId, admissionToken)`을 호출합니다. 실패하면 연결을 종료합니다.
6. 연결 종료 시 `RoomHub.ClientDisconnected(userId, admissionToken)`을 호출합니다.

SignalR 함수는 클라이언트가 아닌 등록된 Unity 방 서버가 호출합니다. Unity 쪽 코드는 이번 변경에 포함되지 않았습니다.

## 파일과 함수별 변경 위치

| 파일 | 함수 또는 속성 | 변경 |
|---|---|---|
| Services/Room/RoomService.Admission.cs (새 파일) | JoinRoom() | 기존 RoomService.cs에서 이동. 정원에 예약 인원을 포함하고, 토큰·기한 반환. 사용자 실제 입장 정보는 갱신하지 않음 |
| 같은 파일 | RegisterServer() | 실행할 때 발급한 서버 토큰으로 Unity 서버 확인. 방당 활성 SignalR 연결 하나 유지 |
| 같은 파일 | ValidateConnection() | 승인 전 예약 검사. 예약을 소비하지 않음 |
| 같은 파일 | ConfirmConnection() | 토큰·사용자·방 검증 후 예약을 실제 입장으로 전환. RoomIndex 배정, 방장 보정. 동일 확인 재전송 허용 |
| 같은 파일 | ClientDisconnected() | 현재 접속 토큰과 일치할 때만 퇴장. 이전 연결의 늦은 종료 알림은 거절 |
| 같은 파일 | ServerDisconnected() | 방 서버 연결 종료 시 예약·실제 인원·사용자 방 정보 정리 |
| 같은 파일 | ExpireReservations(), RemoveReservation(), StopUnusedServer() | 만료/취소 예약 해제. 인원·예약이 모두 없으면 실행 중인 서버 종료, 방은 재시도 가능 상태로 유지 |
| Services/Room/RoomService.cs | 클래스 선언, 생성자 | partial 클래스 및 테스트용 TimeProvider 추가. 기존 파일과 새 파일은 하나의 RoomService 클래스 |
| 같은 파일 | CreateRoom(), EnsureRoomServer(), LeaveRoom() | 사용자 상태 변경을 공유 잠금으로 보호. 여러 요청의 정원 검사·예약·퇴장 충돌 방지 |
| 같은 파일 | InitializeRoom(), LaunchRoomServer() | 방 서버 토큰 생성 및 실행 인자로 전달 |
| 같은 파일 | WaitRoomReady() | 실패 결과를 그대로 반환. 준비 시간 초과 시 프로세스와 서버 토큰 정리 |
| 같은 파일 | LeaveRoom(), LeaveRoomCore() | 예약 상태에서도 같은 leave API로 취소 가능. 예약자가 남아 있으면 방 삭제하지 않음 |
| 같은 파일 | ChangeMaster(), DeleteRoom() | 실제 사용자 없을 때 방장 비움. 방 삭제 시 서버 연결·토큰 제거 |
| Hubs/RoomHub.cs | Register(roomId, serverToken) | 기존 Register(roomId)에서 인자 변경. 유효한 서버 토큰 필요 |
| 같은 파일 | ValidateConnection(), ConfirmConnection(), ClientDisconnected() | Unity 서버용 SignalR 함수 추가 |
| 같은 파일 | GetRoomId(), OnDisconnectedAsync() | 등록한 연결의 방 ID만 사용. 해당 활성 서버 연결 종료 시 정리 |
| Models/RoomAdmission.cs (새 파일) | UserId, RoomId, Token, ExpiresAtUtc | 서버 내부 예약 정보. 응답 목록에는 노출하지 않음 |
| Models/UserInfo.cs | PendingRoomId | 입장 예약 중인 방 ID. CurrentRoomId는 실제 접속 확정 후 사용 |
| Models/RoomInfo.cs | ReservedUsers, ServerToken | 예약 인원과 서버 등록 토큰. ServerToken은 JsonIgnore로 HTTP 응답에서 제외 |
| Responses/Room/JoinRoomResponse.cs | AdmissionToken, ExpiresAtUtc | 클라이언트 접속용 일회성 토큰과 UTC 기한 추가 |
| Services/Auth/UserService.cs | MembershipLock | 로비 이동과 방 상태 변경에 공유 잠금 제공 |
| Services/Lobby/LobbyService.cs | JoinLobby(), LeaveLobby(), 내부 Core 함수 | 공유 잠금 적용. 예약 중에도 로비 퇴장 차단. 예약 취소 후 퇴장 가능 |
| Services/Room/RoomReservationCleanupService.cs (새 파일) | ExecuteAsync() | 1초마다 만료 예약 정리 |
| Program.cs | AddHostedService 등록 | 예약 만료 정리 서비스 실행 |
| Helper/ProcessHelper.cs | LaunchUnityServer() | 선택 인자 serverToken 추가, ArgumentList로 인자 전달, 창 없이 실행. 실행 실패를 호출자에게 전달 |

## 핵심 코드

`RoomService.Admission.cs` → `JoinRoom()`:

```csharp
if (room.Users.Count + room.ReservedUsers >= room.MaxUsers)
    return JoinError("방이 가득 찼습니다.");

reservations.Add(user.Id, admission);
room.ReservedUsers++;
user.PendingRoomId = room.Id;
```

`RoomService.Admission.cs` → `ConfirmConnection()`:

```csharp
if (!room.Users.TryAdd(userId, user))
    return Result(false, "사용자 등록에 실패했습니다.");
RemoveReservation(admission);
user.CurrentRoomId = room.Id;
user.CurrentRoomName = room.Name;
```

## Unity 쪽 다음 작업

- 실행 인자 `-Id`, `-port`에 더해 `-serverToken`을 읽습니다.
- Netcode 서버가 접속을 받을 준비가 된 뒤 `Register(roomId, serverToken)`으로 SignalR에 등록합니다. 이전 한 인자 호출은 수정해야 합니다.
- 접속 승인·접속 완료·접속 종료 시 위 함수들을 호출합니다. 사용자 ID별로 해당 연결의 입장 토큰을 보관합니다.
- 사용자가 접속 전 취소하면 `POST /api/Room/leave`를 호출합니다. 접속 이후 퇴장은 실제 Netcode 연결 종료와 서버의 `ClientDisconnected()` 통지를 연결해야 합니다.
- Unity 메인 스레드 처리와 비동기 승인 연결은 실제 Unity 프로젝트를 확인한 뒤 구현합니다.

예약 기한은 서버 시작 대기 중 120초이며, 접속 정보 반환 시점에 30초로 갱신됩니다. 만료 검사는 API/Hub 호출 시와 1초 주기 정리에서 수행합니다.
ASP.NET 사용자 로그인 방식 및 메모리 저장 구조는 기존 방식입니다. 서버 등록 토큰은 사용자 로그인 인증을 대신하지 않습니다.

## 검증

빌드와 서비스 수준 검사에서 동시 예약 정원 제한, 중복 예약, 잘못된/다른 사용자 토큰, 승인 전 검사, 접속 확정 재전송, 예약 만료/취소/재시도, 늦은 연결 종료, 활성 서버 종료, JSON 토큰 숨김, Unity 실행 실패 시 예약 해제를 확인했습니다.
Unity 실행 파일과 실제 Netcode 클라이언트의 통합 테스트는 아직 수행하지 않았습니다.
기존 LobbyHub 및 미구현 ContentService의 null 관련 컴파일 경고는 남아 있습니다.
