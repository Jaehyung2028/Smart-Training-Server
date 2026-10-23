param(
    [string]$ServerAddress='localhost',
    [ValidateRange(1,65535)][int]$Port=5229
)
$ErrorActionPreference='Stop'
if([Uri]::CheckHostName($ServerAddress.Trim('[',']')) -eq [UriHostNameType]::Unknown){throw 'ServerAddress에는 서버 IP 또는 호스트 이름만 입력해주세요.'}
$taskUri=[UriBuilder]::new('http',$ServerAddress,$Port,'/api/health')
Write-Output '이 PC의 네트워크 주소:'
foreach($taskAdapter in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()){
 if($taskAdapter.OperationalStatus -ne 'Up' -or $taskAdapter.NetworkInterfaceType -eq 'Loopback'){continue}
 foreach($taskAddress in $taskAdapter.GetIPProperties().UnicastAddresses){
  if($taskAddress.Address.AddressFamily -eq 'InterNetwork' -and ![Net.IPAddress]::IsLoopback($taskAddress.Address)){
   Write-Output ('  '+$taskAdapter.Name+' : '+$taskAddress.Address)
  }
 }
}
Write-Output ('API 확인 주소: '+$taskUri.Uri.AbsoluteUri)
try{
 $taskHealth=Invoke-RestMethod -Uri $taskUri.Uri -Method Get -TimeoutSec 8
 if($taskHealth.status -ne 'ok'){throw 'API가 정상 상태를 반환하지 않았습니다.'}
 Write-Output 'API 연결 정상. Unity 클라이언트에서 이 서버 주소로 로그인할 수 있습니다.'
 Write-Output 'UDP 멀티 접속은 클라이언트 두 개로 같은 방에 입장하여 별도로 확인하세요.'
}catch{
 Write-Output 'API에 연결하지 못했습니다. 서버 실행 여부, IP와 포트, 같은 네트워크인지, TCP 수신 허용을 확인하세요.'
 throw
}
