# STEP 11 — 내 정보 조회

## API와 용도
GET /api/Auth/me
Authorization: Bearer <accessToken>

Unity가 화면을 다시 열거나 토큰을 갱신한 뒤 현재 서버 상태를 확인하는 API입니다.
사용자 ID를 요청에 전달할 필요가 없습니다. 인증 토큰의 사용자만 조회합니다.
200 응답은 CurrentUserResponse 객체이며 다음 필드가 포함됩니다.
id, name, currentLobbyId, currentLobbyName, lobbyIndex,
currentRoomId, currentRoomName, roomIndex, pendingRoomId.

currentRoomId: Unity 서버의 접속 확인까지 완료된 방.
pendingRoomId: 아직 접속 확인을 기다리는 예약 방.
소속이 없으면 ID와 이름은 빈 문자열, 인덱스는 -1입니다.
예약 만료는 기존 서버 정리 기능이 반영한 상태를 반환합니다.
조회는 게임 네트워크 연결을 복구하거나 예약 토큰을 다시 발급하지 않습니다.
Unity 접속 예약 토큰이 없다면 기존 방 나가기 API로 예약을 취소하고 다시 예약합니다.

토큰이 없거나 유효하지 않거나 만료되면 401입니다.
만료 유예 기간은 refresh만 허용하며 me 조회는 허용하지 않습니다.
조회 도중 로그아웃으로 사용자가 삭제된 경우에도 401입니다.
토큰 갱신 이후에는 새 토큰으로 조회해야 합니다.
Cache-Control: no-store, Pragma: no-cache로 개인 상태 응답의 저장을 방지합니다.
서버 재시작 시 사용자와 토큰이 사라지는 기존 메모리 저장 방식은 유지됩니다.

## 변경 파일과 함수
- _Controllers/AuthController.cs — Me(): 인증된 사용자 ID로 조회, 캐시 금지, 사용자 삭제 시 401 처리.
- Services/Auth/UserService.Query.cs — GetCurrentUser(): MembershipLock 아래 상태를 새 DTO로 복사.
- Responses/Auth/CurrentUserResponse.cs — 토큰 없는 현재 사용자 응답 DTO.

## 검증
기존 전체 검증에 실제 HTTP 내 정보 조회 검증을 추가했습니다.
비로그인 401, 임의 사용자 ID 무시, 초기 상태, 로비 가입/퇴장, 예약/취소,
독립 스냅샷, 토큰 값 제외, 캐시 금지, 만료 401, 갱신 후 동일 사용자 조회를 확인했습니다.