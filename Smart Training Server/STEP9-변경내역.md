# 9단계: 표준 생성 주소와 공통 API 오류 응답

## 주소 변경

- 권장: `POST /api/Lobby/create`, `POST /api/Room/create`
- 기존: `POST /api/Lobby/creat`, `POST /api/Room/creat`도 같은 함수로 처리합니다.
- 로그인, 입장, 퇴장, 조회 주소 및 인증 요구는 유지합니다.

새로 작성할 Unity 코드는 create 주소를 사용하면 됩니다. 기존 호출은 바로 수정하지 않아도 됩니다.

## 공통 오류 형식

```json
{
  "success": false,
  "message": "로비를 찾을 수 없습니다.",
  "errorCode": "not_found",
  "traceId": "요청 추적 ID"
}
```

| HTTP 상태 | errorCode | 의미 |
|---|---|---|
| 400 | bad_request | 서비스가 요청을 거절함: 중복, 잘못된 상태, 콘텐츠 없음, 서버 실행 실패 등 |
| 400 | validation_failed | 잘못된 JSON, 필수 본문/항목 누락, 데이터 형식 오류 |
| 401 | unauthorized | 인증 누락/오류/만료 또는 갱신 불가 |
| 403 | forbidden | 인증은 되었지만 권한 없음 |
| 404 | not_found | 로비/방/요청 주소 없음 |
| 405 | method_not_allowed | 주소에 지원하지 않는 HTTP 방식 사용 |
| 415 | unsupported_media_type | JSON이 아닌 지원하지 않는 요청 본문 형식 |
| 500 | internal_error | 처리되지 않은 서버 예외 |

HTTP 상태를 유지하고 success/message를 유지합니다. 기존 실패 응답에 있던 빈 user/room 등의 부가 필드는 공통 오류 응답에서 제외합니다.
성공 응답 모델은 변경하지 않습니다. 서비스 내부와 SignalR의 BaseResponse 계약도 변경하지 않습니다.
errorCode는 이번 단계에서는 오류 종류별 공통 코드입니다. 방 정원/중복 이름 같은 세부 원인은 message에 표시됩니다.
예외 내용과 스택은 클라이언트에게 전달하지 않고 서버 로그에 TraceId와 함께 기록합니다.
없는 /api 주소나 빈 오류 응답에도 같은 형식을 적용합니다.

## 파일과 함수별 변경

| 파일 | 함수/위치 | 변경 |
|---|---|---|
| _Controllers/LobbyController.cs | CreateLobby() | HttpPost("create") 추가. 기존 creat 유지 |
| _Controllers/RoomController.cs | CreateRoom() | HttpPost("create") 추가. 기존 creat 유지 |
| 같은 파일 | GetRooms(), GetRoom() | 404 응답에 공통 오류 모델 사용 |
| _Controllers/AuthController.cs | RefreshUnauthorized() | 갱신 실패 401 응답 통일 |
| Services/Auth/UserTokenAuthenticationHandler.cs | HandleChallengeAsync() | 인증 실패 401 응답 통일 |
| Responses/Base/ApiErrorResponse.cs (신규) | Create() | HTTP 상태에 맞는 errorCode/message/traceId 생성 |
| Services/Api/ApiErrorResponseFilter.cs (신규) | OnResultExecuting() | 컨트롤러 및 자동 모델 검증의 오류 결과를 공통 모델로 변환 |
| Services/Api/ApiResponseExtensions.cs (신규) | AddTrainingApiControllers() | 컨트롤러에 오류 필터 등록 |
| 같은 파일 | UseTrainingApiErrors() | 예외 처리와 빈 API 오류 응답 작성 |
| Program.cs | 등록 위치 | 공통 컨트롤러 설정 및 오류 처리 연결 |
| Models/RoomInfo.cs | CreatorUserId 추가 | 서버 내부 생성자 ID 보관. HTTP 응답에서 제외 |
| Services/Room/RoomService.cs | CreateRoomInfo() | 생성자 ID 등록 |
| Services/Room/RoomService.UserCleanup.cs | RemoveUserFromRooms() | 실행 실패로 방장 ID가 초기화되어도 생성자의 빈 방을 로그아웃 시 정리 |

## 핵심 코드

LobbyController.CreateLobby(), RoomController.CreateRoom():

```csharp
[HttpPost("creat")]
[HttpPost("create")]
```

Program.cs:

```csharp
builder.Services.AddTrainingApiControllers();
// Build 이후
app.UseTrainingApiErrors();
```

## 확인 방법

1. Swagger에서 새 create 주소로 로비·방을 생성합니다.
2. 기존 creat 주소에서도 같은 동작인지 확인합니다.
3. 토큰 없이 요청하면 401과 unauthorized를 확인합니다.
4. 없는 방을 조회하면 404와 not_found를 확인합니다.
5. JSON 구문을 잘못 보내면 400과 validation_failed를 확인합니다.
6. 오류 문의 시 traceId를 함께 사용하면 서버 로그와 대조할 수 있습니다.

## 검증

로컬 HTTP 요청으로 표준/기존 생성 주소, 입력 검증, 인증·갱신 실패, 없는 방/주소, 잘못된 HTTP 방식, 지원하지 않는 본문 형식, 권한 부족, 서버 예외의 오류 응답을 확인했습니다.
로그인→로비 생성→입장→콘텐츠로 방 생성→방 목록 조회→토큰 갱신→로그아웃→닉네임 재사용 흐름도 실제 HTTP로 검증했습니다.
Unity 파일이 없는 방 입장은 실패하며 예약이 남지 않는지 확인했습니다. 실제 Netcode 입장은 아직 검증하지 않았습니다.
통합 검증에서 서버 실행 실패 후 방장 ID가 초기화된 빈 방이 로그아웃 시 남는 문제를 발견했습니다. 생성자 ID를 따로 보관하여 해당 빈 방도 제거하도록 보완했습니다. 다른 참가자나 예약자가 있는 방은 유지합니다.
기존 토큰/유예/로그아웃/프로세스 종료/예약 검증도 함께 실행했습니다. 테스트 전용 예외·권한 검증 주소는 검증 도구 안에만 있고 원본 서버에는 추가하지 않습니다.
