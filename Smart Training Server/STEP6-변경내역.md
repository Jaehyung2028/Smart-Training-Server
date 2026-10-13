# 6단계: 로그아웃과 사용자 정보 정리

## 추가 API

`POST /api/Auth/logout`

- 로그인 응답의 사용자 토큰을 `Authorization: Bearer ...`로 전달합니다.
- 요청 본문은 필요하지 않습니다. 본문에 다른 UserId를 보내도 사용하지 않습니다.
- 성공 시 `200 OK`, `{ "success": true, "message": "로그아웃되었습니다." }`를 반환합니다.
- 인증 누락/오류/만료 또는 이미 무효화된 토큰은 `401 Unauthorized`입니다.

## 정리 순서

1. 해당 사용자의 방 입장 예약을 취소합니다.
2. 방 참가 목록에서 제거하고 방장 변경을 처리합니다. 해당 사용자가 만든 빈 방도 제거합니다.
3. 참가자와 예약자가 모두 없는 방은 삭제하고, 실행 중이거나 시작 중인 Unity 프로세스가 있으면 종료합니다.
4. 로비 참가 목록에서 제거합니다. 사용자와 방이 모두 없는 로비만 삭제합니다.
5. 사용자 인증 세션을 모두 무효화하고 사용자 정보를 제거합니다.
6. 연결 정보가 남아 있는 Unity 방 서버에 정확한 사용자 ID와 기존 입장 토큰을 담은 `DisconnectUser` 이벤트를 보냅니다.

ASP.NET의 상태 정리는 공유 잠금 아래 수행하고 SignalR 전송은 잠금 밖에서 수행합니다.
이벤트 전송 실패 시 로그를 기록하며, 이미 제거한 사용자나 토큰을 되살리지 않습니다.
같은 닉네임으로 다시 로그인할 수 있으며 새 사용자 ID와 토큰이 발급됩니다. 이전 사용자의 늦은 연결 종료/중복 정리가 새 사용자를 제거하지 않습니다.

## 파일과 함수별 변경

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| _Controllers/AuthController.cs | Logout() 추가 | 인증된 사용자 ID만 사용하여 로그아웃 실행 |
| 같은 파일 | 생성자 | UserLifecycleService 주입 추가 |
| Services/Auth/UserLifecycleService.cs (신규) | LogoutAsync() | 방→로비→사용자/토큰 순서로 정리하고 Unity 종료 이벤트 전송 |
| Services/Auth/UserService.Session.cs | RevokeSessions() 추가 | 해당 사용자에게 발급한 세션 제거 |
| 같은 파일 | RemoveUser() 추가 | 세션을 제거한 뒤 사용자 삭제, 닉네임 재사용 허용 |
| Services/Room/RoomService.UserCleanup.cs (신규) | RemoveUserFromRooms() | 예약·참가 정보 정리, 방장 변경, 빈 방 삭제, 정확한 연결 종료 알림 정보 수집 |
| Services/Room/RoomService.cs | DeleteRoom() | 시작 중인 서버도 종료. 로비가 이미 없는 경우에도 전역 방 제거 가능 |
| Services/Lobby/LobbyService.cs | RemoveUserFromLobbies() 추가 | 참가 목록과 사용자 로비 정보 정리 |
| 같은 파일 | RemoveEmptyLobby() 추가 | 로비에서 먼저 나간 사용자의 마지막 빈 방을 삭제한 뒤 빈 로비도 정리 |
| 같은 파일 | LeaveLobbyCore() | 방이 남아 있는 빈 로비를 바로 삭제하지 않도록 보완 |
| Program.cs | 서비스 등록 | UserLifecycleService singleton 등록 |

partial 파일은 같은 UserService/RoomService 클래스의 일부입니다. 핵심 로그인 및 예약 함수는 기존 동작을 유지합니다.

## 핵심 코드

AuthController.cs → Logout():

```csharp
[HttpPost("logout")]
[Microsoft.AspNetCore.Authorization.Authorize]
public async Task<ActionResult<BaseResponse>> Logout()
```

UserLifecycleService.cs → LogoutAsync():

```csharp
lock (users.MembershipLock)
{
    notices = rooms.RemoveUserFromRooms(userId);
    lobbies.RemoveUserFromLobbies(userId);
    users.RemoveUser(userId);
}
```

## 직접 확인

1. Swagger에서 닉네임 로그인 후 accessToken을 Authorize에 입력합니다.
2. 로비에 입장하고 방을 생성합니다.
3. `POST /api/Auth/logout`을 호출합니다. 요청 본문은 없습니다.
4. 같은 토큰으로 로비/방 조회를 요청하면 401인지 확인합니다.
5. 같은 닉네임으로 다시 로그인하면 성공하며 새 ID와 토큰이 발급되는지 확인합니다.
6. 다른 참가자가 있는 방은 유지되며, 방장이 나가면 남은 실제 참가자에게 방장이 넘어갑니다.

## Unity 연결 범위

이번 작업은 명시적인 로그아웃 시 ASP.NET 상태를 정리하는 기능입니다.
Unity 방 서버가 아직 구현되지 않았으므로 실제 Netcode 연결을 끊는 수신 코드는 없습니다.
나중에 Unity SignalR 연결에서 `DisconnectUser(userId, admissionToken)`을 수신하고, 저장한 해당 접속의 사용자 ID/토큰이 일치할 때만 Netcode 연결을 종료해야 합니다.
이벤트 발송은 실제 연결 종료 완료를 보장하지 않습니다. 마지막 사용자 퇴장 시에는 방 서버 프로세스 종료도 수행합니다.
강제 앱 종료·네트워크 단절을 자동 감지하는 기능, 만료한 사용자 세션의 자동 퇴장, 토큰 갱신 및 기존 ID로 재접속은 이후 작업입니다.

## 검증

빌드와 실제 로컬 HTTP 검증에서 인증 없는 로그아웃 401, 토큰 무효화, 요청 본문의 사용자 ID 위조 방지, 예약 취소, 다른 참가자 유지, 방장 변경, 빈 방/로비 제거, 닉네임 재사용, 늦은 종료 알림과 중복 정리의 안전성을 확인했습니다.
Unity 이벤트 전송의 인자와 전송 실패 시 정리 유지도 대체 전송 객체로 검증했습니다. 시작 준비 대기 중 로그아웃이 대기를 해제하고 늦게 시작한 서버가 등록되지 않는지도 확인했습니다.
기존 콘텐츠·인증·방 조회·예약 검증도 함께 실행했습니다. 실제 Unity 연결 테스트는 수행하지 않았습니다.
