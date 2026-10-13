# 5단계: 사용자 로그인 토큰 인증

## 변경한 동작

닉네임 로그인 성공 응답에 `accessToken`, `tokenType`, `expiresAtUtc`가 추가되었습니다.
로그인은 인증 없이 호출할 수 있고, 로비/방의 생성·입장·퇴장·조회 API는 모두 사용자 토큰이 필요합니다.
기존 요청 본문의 UserId는 유지하되 컨트롤러에서 인증된 사용자 ID로 덮어씁니다. 클라이언트는 UserId를 생략할 수 있습니다.
다른 사용자의 UserId를 보내도 그 사용자를 대신 조작할 수 없습니다.

```text
Authorization: Bearer 로그인_응답의_accessToken
```

기본 만료 시간은 60분입니다. 원한다면 appsettings.json의 기존 Auth 설정에 `AccessTokenLifetimeMinutes`를 추가합니다. 허용 범위는 1~1440분입니다.
설정 검증은 앱 시작 시에도 적용됩니다.

## 구현 방식

현재 사용자와 방 정보가 메모리에 저장되는 단일 서버 구조에 맞춰, 임의의 32바이트로 만든 불투명한 Bearer 토큰을 사용합니다. JWT가 아닙니다.
토큰 원문은 로그인 응답에만 전달하고, 서버에는 SHA-256 해시와 사용자 ID·만료 시각을 저장합니다.
토큰은 UserInfo에 저장하지 않으므로 로비/방 사용자 목록에서 유출되지 않습니다.
서버를 재시작하면 사용자 정보와 인증 토큰은 무효화됩니다.
현재 닉네임 로그인은 임시 사용자 생성 방식입니다. 비밀번호 계정 로그인, 만료 후 동일 사용자 복구, 갱신 토큰 및 로그아웃/회원 정리는 이번 범위에 포함하지 않습니다. 같은 닉네임 중복 거절 규칙은 유지됩니다.
인증 토큰 만료는 API 인증을 거절하는 기준이며, 이미 연결된 Netcode 사용자를 자동으로 퇴장시키지는 않습니다.

## 변경 파일과 함수

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| Services/Auth/UserService.cs | Login(), LoginCore() | 공유 잠금으로 중복 닉네임 검사·사용자 생성 보호. 성공 시 세션 토큰 발급 |
| 같은 파일 | 생성자 | TimeProvider와 토큰 기한 검증 추가 |
| Services/Auth/UserService.Session.cs (신규) | CreateSession() | 임의 토큰 발급 및 해시 저장 |
| 같은 파일 | FindByAccessToken() | 등록/만료/사용자 존재 검사 |
| 같은 파일 | RemoveExpiredSessions() | 만료 세션 정리 |
| Services/Auth/UserTokenAuthenticationHandler.cs (신규) | HandleAuthenticateAsync() | Authorization 헤더 검사 후 NameIdentifier에 사용자 ID 등록 |
| 같은 파일 | HandleChallengeAsync() | 인증 누락/오류 시 JSON 401 및 WWW-Authenticate: Bearer 반환 |
| Services/Auth/SwaggerUserAuthenticationFilter.cs (신규) | Apply() | 인증이 필요한 API에만 Swagger 보안 표시 |
| Options/AuthOptions.cs | AccessTokenLifetimeMinutes | 기본 60분 설정 추가 |
| Responses/Auth/LoginResponse.cs | AccessToken, TokenType, ExpiresAtUtc | 로그인 응답 필드 추가 |
| _Controllers/AuthController.cs | Login() | AllowAnonymous 및 응답 캐시 금지 헤더 |
| _Controllers/LobbyController.cs | 클래스 Authorize, JoinLobby(), LeaveLobby(), GetAuthenticatedUserId() | 모든 로비 API 인증 요구. 사용자 ID를 토큰에서 획득 |
| _Controllers/RoomController.cs | 클래스 Authorize, CreateRoom(), JoinRoom(), LeaveRoom(), GetAuthenticatedUserId() | 모든 방 API 인증 요구. 사용자 ID를 토큰에서 획득 |
| Program.cs | 인증 등록·미들웨어·Swagger | AddAuthentication, AddAuthorization, UseAuthentication → UseAuthorization 순서와 Swagger 토큰 입력 추가 |

두 사용자 컨트롤러에 Authorize를 적용했습니다. Unity 방 서버용 RoomHub는 기존 서버 등록 토큰으로 확인하는 흐름을 유지합니다.
사용자 인증 토큰, 방 입장 토큰, Unity 서버 등록 토큰은 서로 대신 사용할 수 없습니다.

## 핵심 코드

UserService.cs → LoginCore():

```csharp
var session = CreateSession(user!.Id);
return new LoginResponse
{
    Success = true,
    Message = "로그인 성공",
    User = user,
    AccessToken = session.Token,
    ExpiresAtUtc = session.ExpiresAtUtc
};
```

LobbyController.JoinLobby()/LeaveLobby(), RoomController.CreateRoom()/JoinRoom()/LeaveRoom():

```csharp
request.UserId = GetAuthenticatedUserId();
```

Program.cs:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

## Swagger에서 확인

1. 서버 실행 후 개발 환경의 Swagger를 엽니다.
2. `POST /api/Auth/login`에서 닉네임으로 로그인합니다.
3. 응답의 accessToken을 복사합니다.
4. Swagger의 Authorize 버튼에서 토큰만 입력합니다. Bearer 접두어는 자동으로 붙습니다.
5. 로비 생성/입장과 방 생성/조회/입장/퇴장을 요청합니다. UserId는 생략해도 됩니다.
6. 토큰을 제거하면 로비/방 API가 401인지 확인합니다.
7. 다른 사용자의 UserId를 본문에 넣어도 토큰 소유자 기준으로 처리됩니다.

토큰 기한은 UTC expiresAtUtc입니다. Unity에서는 로그인 응답을 보관하고 모든 로비/방 HTTP 요청에 Authorization 헤더를 추가해야 합니다.

## 검증

빌드와 실제 로컬 HTTP 요청으로 익명 로그인, 토큰 발급/만료, 잘못된 토큰/스킴 거절, 보호 API의 401, UserId 위조 방지, UserId 없는 입장/퇴장, 다른 사용자의 예약 취소 방지, 입장/서버 토큰의 사용자 인증 사용 거절, Swagger 보안 표시와 로그인 캐시 금지를 확인했습니다.
이전 콘텐츠·방 조회·입장 예약·접속 확정·퇴장 검증도 함께 통과했습니다.
실제 Unity 연결 테스트는 수행하지 않았습니다. 기존 LobbyHub.cs의 null 관련 경고는 남아 있습니다.

인증 미들웨어와 스킴 구성 참고: https://learn.microsoft.com/en-gb/aspnet/core/security/authentication/?view=aspnetcore-9.0
