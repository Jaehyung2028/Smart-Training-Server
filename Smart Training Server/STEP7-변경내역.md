# 7단계: 사용자 토큰 갱신과 만료 세션 정리

## API와 정책

`POST /api/Auth/refresh`

- 본문 없이 `Authorization: Bearer 현재_사용자_토큰`을 보냅니다.
- 유효한 토큰 또는 만료 후 기본 5분 이내의 사용자 토큰만 갱신할 수 있습니다.
- 성공 시 로그인과 같은 응답 형태로 새 accessToken과 expiresAtUtc를 반환합니다. 사용자 ID와 로비/방 참가 상태를 유지합니다.
- 이전 토큰은 즉시 무효화됩니다. 이전 토큰으로 재갱신하거나 일반 API를 호출할 수 없습니다.
- 토큰 누락·오류·회전된 옛 토큰·유예 기한 초과·삭제된 사용자이면 401입니다.
- 방 입장 토큰과 서버 등록 토큰은 갱신에 사용할 수 없습니다.
- 로그인과 갱신 응답은 캐시하지 않습니다. Swagger의 Authorize에 현재 토큰을 입력한 뒤 호출하고, 성공 후 새 토큰으로 바꿉니다.

이 API는 만료 토큰을 유예 기간 안에 검사해야 하므로 일반 Authorize 대신 AllowAnonymous와 함수 내부의 명시적 토큰 검사를 사용합니다. 인증 없이 갱신을 허용한다는 뜻은 아닙니다.

## 만료 흐름

1. 토큰 유효 기간(기본 60분): 일반 API와 갱신 모두 허용.
2. 만료 후 유예(기본 5분): 일반 API와 로그아웃은 401. 참가 상태는 유지하며 refresh만 허용.
3. 유예 기한 이상: refresh도 401. 10초 주기 정리 서비스가 방 예약/참가, 로비 참가, 사용자/세션을 정리.
4. 정리 완료 후 같은 닉네임으로 새로 로그인 가능. 새 사용자 ID를 발급.

만료 및 유예 경계는 서버의 UTC 시각으로 판정합니다. 유예 종료 후 정리까지 약 10초의 검사 주기 차이가 있을 수 있습니다.
Unity에서는 만료 전에 갱신하고, 응답의 새 토큰으로 이후 API 요청을 보내는 흐름을 연결해야 합니다.
일시적인 만료 때문에 즉시 훈련 상태를 삭제하지 않도록 5분 유예를 기본 정책으로 적용했습니다.

## 설정

기존 appsettings.json의 Auth에 다음 항목을 선택적으로 추가할 수 있습니다. 생략하면 아래 기본값을 사용합니다.

```json
"AccessTokenLifetimeMinutes": 60,
"SessionGraceMinutes": 5
```

토큰 기한은 1~1440분, 유예 시간은 0~60분입니다. 0분이면 만료 즉시 갱신이 거절되고 다음 정리 주기에 사용자를 제거합니다.

## 변경 파일과 함수

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| _Controllers/AuthController.cs | Refresh(), RefreshUnauthorized() 추가 | 헤더의 사용자 토큰 검사, 갱신 응답 또는 401 |
| Services/Auth/UserService.Session.cs | RefreshSession() 추가 | 같은 사용자로 토큰 회전. 이전 토큰 제거 |
| 같은 파일 | FindByAccessToken() | 만료 토큰은 일반 인증 거절하되 유예 갱신에 필요한 세션은 유지 |
| 같은 파일 | CreateSession() | 기존의 즉시 만료 세션 삭제 호출 제거. 상태 정리는 Lifecycle로 통합 |
| 같은 파일 | GetUsersPastSessionGrace() 추가 | 유효/유예 세션이 없는 사용자 선정 |
| Services/Auth/UserLifecycleService.cs | CleanupExpiredUsersAsync() 추가 | 기존 로그아웃과 같은 방→로비→사용자 정리. 갱신과 동일 잠금 안에서 대상 선정 및 삭제 |
| 같은 파일 | SendDisconnectNoticesAsync() 추가 | 로그아웃과 만료 정리가 공유하는 Unity 종료 요청 전송 |
| Services/Auth/UserSessionCleanupService.cs (신규) | ExecuteAsync() | 10초 주기로 만료 사용자 정리, 오류 기록 후 다음 주기 계속 |
| Options/AuthOptions.cs | SessionGraceMinutes | 기본 5분 설정 |
| Services/Auth/UserService.cs | 생성자 | 유예 설정 범위 검사 |
| Program.cs | Auth 설정 검증, HostedService 등록 | 유예 설정 검증과 정리 서비스 자동 시작 |
| Services/Auth/SwaggerUserAuthenticationFilter.cs | Apply() | refresh API도 Swagger에서 Bearer 헤더를 입력하도록 표시 |

## 핵심 코드

UserService.Session.cs → RefreshSession():

```csharp
var next = CreateSession(user.Id);
sessions.Remove(HashToken(token));
```

UserLifecycleService.cs → CleanupExpiredUsersAsync():

```csharp
lock (users.MembershipLock)
{
    foreach (var userId in users.GetUsersPastSessionGrace())
    {
        notices.AddRange(rooms.RemoveUserFromRooms(userId));
        lobbies.RemoveUserFromLobbies(userId);
        users.RemoveUser(userId);
    }
}
```

## 검증과 한계

실제 로컬 HTTP 요청으로 유예 중 갱신 성공, 사용자 ID 유지, 새 토큰 인증, 이전 토큰 무효화, 잘못된 토큰의 401을 확인했습니다.
제어 가능한 테스트 시계로 정확한 만료/유예 경계, 동시 갱신 하나만 성공, 갱신한 사용자 유지, 만료 참가자의 방·로비 제거, 마지막 참가자 제거 시 방·로비 삭제, 닉네임 재사용과 반복 정리 안전성을 검증했습니다.
기존 콘텐츠·예약·조회·인증·로그아웃 검사도 함께 실행했습니다.
실제 Unity 연결 테스트는 수행하지 않았습니다. Unity 수신 구현 전에는 DisconnectUser 이벤트 발송만으로 Netcode 연결 종료를 보장할 수 없습니다.
서버 메모리 세션 방식은 유지됩니다. 서버 재시작 이후 기존 사용자 ID 복구나 계정 인증은 아직 제공하지 않습니다.
