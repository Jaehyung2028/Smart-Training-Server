# 같은 네트워크의 다른 PC에서 접속하기

## 서버 PC

1. ASP.NET 프로젝트를 `http` 프로필로 실행합니다. 기본 API 포트는 `appsettings.json`의 `Server:Port = 5229`입니다.
2. 실행 콘솔의 `[접속] 다른 PC에서 입력할 API 주소 후보`를 확인합니다. 이 주소를 클라이언트 PC에 입력합니다. IP는 네트워크에 따라 달라지므로 과거 문서의 IP를 그대로 사용하지 않습니다.
3. 주소 후보가 여러 개면 클라이언트와 같은 네트워크의 주소를 사용합니다. `Room:AdvertisedAddress`에 해당 IP만 지정하면 방 서버 주소도 고정할 수 있습니다. 예: `192.168.1.20`이며 `http://`나 포트는 넣지 않습니다.
4. 전용 서버 실행 파일과 `_Data`·DLL이 함께 있는지 확인합니다. API의 `Room:ServerExecutablePath`는 실제 방 서버 실행 파일과 일치해야 합니다. 로비 입장에는 Unity 실행 파일이 필요하지 않습니다.

`Room:AdvertisedAddress`가 빈 문자열이면 연결된 어댑터 중 IPv4 기본 게이트웨이가 있는 주소를 우선합니다. 연결되지 않은 어댑터·루프백·터널·169.254 대역은 자동 후보에서 제외합니다. 여러 네트워크가 있으면 자동 선택이 상대 PC의 접근 경로를 보장하지 않으므로 위 명시 설정을 사용합니다. 사용할 LAN IPv4가 없으면 같은 PC 접속용 루프백으로 동작합니다.

`Room:ServerApiBaseUrl`은 Unity 전용 서버가 같은 PC의 API에 연결하는 주소입니다. 기본 `http://127.0.0.1:5229`를 유지하고, API 포트를 바꿨다면 이 포트도 맞춥니다. 이 값에 클라이언트 PC의 IP를 넣지 않습니다.

## 클라이언트 PC

1. 통합 빌드 결과의 `Client` 폴더 전체를 복사합니다. `Smart Training Client.exe` 하나만 복사하지 않습니다.
2. 브라우저에서 `http://서버PC의IP:5229/api/health`를 열어 `status: ok`가 나오는지 확인합니다.
3. 클라이언트를 실행하고 로그인 화면의 서버 주소에 `http://서버PC의IP:5229`를 입력합니다. 다른 PC에서 `localhost`를 입력하면 해당 클라이언트 PC를 가리킵니다.
4. 서로 다른 닉네임으로 같은 콘텐츠 로비·방에 입장합니다.

읽기 전용 진단 도구도 사용할 수 있습니다. 아래 IP는 예시이므로 서버 콘솔에 표시된 값으로 바꿉니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Networking/Get-ConnectionInfo.ps1 -ServerAddress 192.168.1.20 -Port 5229
```

클라이언트 PC에는 `Get-ConnectionInfo.ps1` 파일만 복사해서 같은 명령으로 실행할 수 있습니다. 이 도구는 주소 조회와 `/api/health` 요청만 수행하며 시스템 설정을 바꾸지 않습니다.

## 방화벽과 포트

API는 TCP 5229, Unity 방 서버는 기본 6000부터 방별로 할당되는 UDP 포트를 사용합니다. 로비 연결과 HTTP 확인이 성공해도 UDP가 막혀 있으면 방 접속은 실패할 수 있습니다. 실제 UDP 포트는 API의 전용 서버 실행 로그와 클라이언트 방 접속 안내에서 확인합니다.

서버 PC의 Windows 방화벽 수신 규칙에서 API TCP 포트와 **실제로 실행되는 Unity 서버 프로그램의 UDP 수신**을 허용합니다. 아래는 신뢰하는 사설 LAN에서 관리자가 선택해 실행할 수 있는 예시이며 자동 실행하지 않습니다.

```powershell
New-NetFirewallRule -DisplayName 'Smart Training API LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5229 -Profile Private -RemoteAddress LocalSubnet
New-NetFirewallRule -DisplayName 'Smart Training Unity UDP LAN' -Direction Inbound -Action Allow -Protocol UDP -Program 'C:\실제경로\RoomServer\Smart Training Room.exe' -Profile Private -RemoteAddress LocalSubnet
```

프로그램 경로와 API 포트를 실제 값으로 바꾸고, 현재 연결이 해당 Private 프로필인지 확인합니다. 방화벽 전체를 끄거나 모든 프로그램의 모든 포트를 열지 않습니다. 공용 Wi-Fi나 게스트 네트워크의 클라이언트 격리가 있으면 같은 공유기에 연결해도 PC끼리 통신하지 못할 수 있습니다.

이 안내는 같은 LAN의 개발용 HTTP 접속을 대상으로 합니다. 인터넷 원격 접속·공유기 포트 전달·정식 배포 구성은 별도 작업입니다. 동일 PC의 LAN 주소로 접속하는 확인과 물리적으로 다른 PC에서의 확인은 구분합니다.
