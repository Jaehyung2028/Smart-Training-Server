# STEP33 — 서버 IP 입력으로 접속하기

## 실행과 접속
1. ASP.NET 프로젝트를 평소처럼 실행합니다. Visual Studio에서는 http 프로필을 선택합니다.
2. 서버 PC의 Swagger 주소는 http://localhost:5229/swagger입니다.
3. Unity 에디터 또는 Builds/Client/Smart Training Client.exe를 실행합니다.
4. 같은 PC에서는 http://localhost:5229, 다른 PC에서는 http://서버PC의IPv4주소:5229를 입력합니다.
5. 서버 연결 확인 후 서로 다른 닉네임으로 로그인하고 같은 로비와 방에 들어갑니다.

현재 개발 PC에서 확인한 주소는 http://192.168.45.118:5229입니다. IP가 변경되면 새 주소를 입력하세요.
별도 LAN 실행 스크립트나 LAN 전용 클라이언트 폴더는 사용하지 않습니다.
클라이언트를 다른 PC로 옮길 때는 Builds/Client 전체 폴더를 복사합니다.

## 구현
- Program.cs의 ConfigureKestrel(): ListenAnyIP로 모든 인터페이스에서 API 접속을 받습니다.
- API 포트는 appsettings.json의 Server:Port이며 기본값은 5229입니다.
- HTTP를 HTTPS로 강제 이동하지 않습니다.
- Unity 방 서버는 기존처럼 UDP 접속을 받고 서버 PC의 IP를 자동으로 전달합니다.
- 네트워크 어댑터가 여러 개라면 Room:AdvertisedAddress에 사용할 IP를 지정할 수 있습니다.
- RoomClientBuild.Build(): 하나의 개발 클라이언트를 Builds/Client에 만듭니다.
- Unity HTTP 옵션은 Development Only이며 해당 클라이언트는 Development Build입니다.
- RoomEndpoint.TryValidate(): 접속용 주소로 쓸 수 없는 값이나 잘못 전달된 localhost를 안내합니다.
- TrainingApiClient.Send(): 요청 시작 오류 후에도 다시 요청할 수 있게 정리합니다.

포트를 변경하면 Room:ServerApiBaseUrl의 로컬 API 포트도 맞춰주세요.
예: Server:Port가 5230이면 Room:ServerApiBaseUrl은 http://127.0.0.1:5230입니다.

## 다른 PC에서 연결되지 않을 때
다른 PC 브라우저에서 http://서버IP:5229/api/health에 접속하여 status가 ok인지 확인합니다.
서버 PC의 방화벽에서 TCP 5229와 Unity 방 서버의 UDP 수신 연결이 허용되어야 합니다.
UDP 포트는 현재 6000부터 방마다 할당되며 방 화면과 서버 실행 로그에서 실제 포트를 확인할 수 있습니다.
Unity 방 서버 실행 경로는 bin/Debug/net8.0/RoomServer/Smart Training Room.exe입니다.
같은 네트워크인지, 공유기의 무선 클라이언트 격리로 통신이 막히지 않는지도 확인합니다.

이 구성은 HTTP를 사용하는 현재 개발용 구성입니다. 인터넷 운영 배포에는 HTTPS 등 배포 설정을 적용해야 합니다.
공식 Unity HTTP 설정 설명: https://docs.unity3d.com/cn/6000.0/ScriptReference/InsecureHttpOption.html
자동 검증은 동일 PC의 실제 LAN 주소로 수행하며 물리적으로 다른 PC에서의 접속은 별도 확인이 필요합니다.
