# STEP 12 — 실행 설정 검증과 방 접속 주소

## 변경 내용
Room:AdvertisedAddress를 추가했습니다. 빈 문자열이면 기존 로컬 IP 자동 선택입니다.
주소를 지정하면 RoomService.InitializeNetwork()에서 그 주소를 방 IpAddress에 넣습니다.
방 참가 응답 room.ipAddress와 방 조회의 접속 주소에도 반영됩니다.
주소는 IP 또는 호스트 이름만 입력합니다. http://, https://, 포트, 공백은 넣지 않습니다.
예: 192.168.1.20 또는 training.example.com.

appsettings.json 예:
```json
"Room": {
  "AdvertisedAddress": "192.168.1.20",
  "ServerExecutablePath": "RoomServer/Smart Training Room.exe",
  "ServerReadyTimeoutSeconds": 60,
  "MinUsers": 1,
  "MaxUsers": 8,
  "NameMinLength": 2,
  "NameMaxLength": 30,
  "PasswordMaxLength": 20,
  "AllowDuplicateName": true
}
```
현재 기본값은 빈 문자열입니다. 실제 사용할 주소는 개발 환경에 맞춰 지정합니다.
이 설정은 Unity 방 서버 주소이며 ASP.NET API의 HTTP 바인딩 주소를 변경하지 않습니다.
기존 Server:Port 값도 HTTP 바인딩을 직접 변경하지 않습니다. ASP.NET 실행 프로필/Urls 설정을 사용합니다.
주소 지정은 접속 가능성을 보장하지 않습니다. 실제 Unity 서버의 포트 수신과 네트워크 경로는 Unity 구현 후 확인해야 합니다.
외부 포트 변환 설정은 이번 단계에 포함되지 않습니다.

## 시작 시 설정 검증
Options/TrainingOptionsExtensions.cs의 AddTrainingOptions()로 설정 등록을 통합했습니다.
Program.cs에서 이 함수를 호출합니다. ValidateOnStart로 다음 오류를 서버 시작 시 확인합니다.
- Auth: 이름 최소/최대 길이, 토큰 수명 1~1440분, 갱신 유예 0~60분.
- Lobby: 최대 로비 수 1 이상, 이름 최소/최대 길이.
- Room: 최소 인원 1 이상 및 최대 인원 이상 관계, 이름 길이, 비밀번호 최대 길이 음수 금지.
- Room: 실행 경로 필수, 준비 대기 1~300초, 접속 주소 형식.
Unity 실행 파일 존재 여부는 시작 검증 조건에 넣지 않았습니다. Unity가 없어도 API 개발이 가능합니다.
실제 실행 시 파일 존재 여부는 기존 ProcessHelper.LaunchUnityServer()에서 검사합니다.
상대 실행 경로는 AppContext.BaseDirectory(서버 실행 출력 폴더) 기준입니다.

## 상태 확인 API
GET /api/health
인증 없이 200 {"status":"ok"}를 반환하고 Cache-Control: no-store를 사용합니다.
_Controllers/HealthController.cs — GetStatus().
이 API는 ASP.NET 서버가 HTTP 요청에 응답한다는 확인입니다.
Unity 실행 파일, Unity 방 서버 또는 멀티플레이 준비 완료를 뜻하지 않습니다.
내부 경로, 토큰, 사용자 목록은 반환하지 않습니다.

## 검증
빌드 성공 및 전체 검증 235개 통과.
시작 검증기(IStartupValidator)를 통해 정상 설정과 15개 잘못된 설정을 검증했습니다.
빈 주소, IPv4, IPv6, 호스트 이름 형식과 방 참가 응답 주소 반영을 확인했습니다.
인증 없는 HTTP 상태 확인과 기존 로그인/로비/방/토큰/프로세스 검증도 통과했습니다.
Unity 클라이언트와 실제 방 서버 간 접속은 아직 검증 대상이 아닙니다.