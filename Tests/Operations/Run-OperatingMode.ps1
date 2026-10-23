param([string]$ServerProject,[string]$ClientExecutable,[string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskServerRoot=Join-Path $taskRoot 'SmartTrainingServer'
$taskClientExe=Join-Path $taskRoot 'UnityClient/Builds/Client/Smart Training Client.exe'
$taskRun=Join-Path $taskRoot ('unified-evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if($ServerProject){$taskServerRoot=[IO.Path]::GetFullPath($ServerProject)}
if($ClientExecutable){$taskClientExe=[IO.Path]::GetFullPath($ClientExecutable)}
if($EvidenceRoot){$taskRun=Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) (Get-Date -Format 'yyyyMMdd-HHmmss')}
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskApi='http://127.0.0.1:51454'
$taskServer=$null; $taskPlayers=@(); $taskIds=@(0,0,0); $taskOld=@{}
$taskSettings=@{ASPNETCORE_ENVIRONMENT='Development'; Server__Port='51454'; Room__ServerApiBaseUrl=$taskApi; Room__AdvertisedAddress='127.0.0.1'; SMART_TRAINING_SERVER_PROBE='1'}
function Read-Player([int]$index){
 $file=Join-Path $taskRun ($index.ToString()+'/state.json')
 if(!(Test-Path -LiteralPath $file)){return $null}
 try{return (Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json)}catch{return $null}
}
function Wait-Player([int]$index,[scriptblock]$condition,[string]$name){
 $end=[DateTime]::UtcNow.AddSeconds(45)
 while([DateTime]::UtcNow -lt $end){
  $state=Read-Player $index
  if($state -and $state.error){throw ('Probe error: '+$state.error)}
  if($state -and (& $condition $state)){return $state}
  if($taskPlayers[$index].HasExited){throw ('Player exited: '+$index)}
  Start-Sleep -Milliseconds 100
 }
 $state=Read-Player $index
 throw ($name+' timed out: '+($state | ConvertTo-Json -Compress))
}
function Send-Player([int]$index,[string]$action,[string]$value='', [string]$lobbyId='', [bool]$enabled=$false){
 $taskIds[$index]++
 $folder=Join-Path $taskRun $index.ToString()
 $temp=Join-Path $folder 'command.tmp'
 @{id=$taskIds[$index];action=$action;value=$value;lobbyId=$lobbyId;enabled=$enabled} | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
 Move-Item -LiteralPath $temp -Destination (Join-Path $folder 'command.json') -Force
 return $taskIds[$index]
}
function Check([bool]$ok,[string]$name){if(!$ok){throw $name}; Write-Output ('MULTIPROCESS PASS: '+$name)}
function Api-Post([string]$path,$body){Invoke-RestMethod ($taskApi+$path) -Method Post -Headers $taskAuth -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress)}
$taskSettings['Operations__OpenBrowserOnStart']='false';$taskSettings['Operations__PersistHistory']='true'
$taskSettings['Operations__HistoryPath']=Join-Path $taskRun 'history.json';$taskSettings['SMART_TRAINING_SERVER_PROBE']='0'
$taskOpsHeaders=@{'X-Operations-Request'='1';Origin=$taskApi};$taskRound=0
function Ops {Invoke-RestMethod ($taskApi+'/api/operations')}
function Mode([bool]$enabled,[string]$reason,[long]$revision){Invoke-RestMethod ($taskApi+'/api/operations/lobbies/sample-training/mode') -Method Post -Headers $taskOpsHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{mode=$(if($enabled){'maintenance'}else{'running'});atUtc=$null;reason=$reason;expectedRevision=$revision}|ConvertTo-Json -Compress)))}
function Root { (Ops).lobbies|Where-Object {$_.id -eq 'sample-training'} }
function Start-Api {
 $script:taskRound++;$dll=Join-Path $taskServerRoot 'bin/Debug/net8.0/Smart Training Server.dll'
 $script:taskServer=Start-Process dotnet -ArgumentList ('"'+$dll+'"') -WorkingDirectory $taskServerRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun ('server-'+$taskRound+'.log')) -RedirectStandardError (Join-Path $taskRun ('errors-'+$taskRound+'.log'))
 $ready=$false
 for($n=0;$n -lt 100;$n++){try{$null=Invoke-RestMethod ($taskApi+'/api/health');$ready=$true;break}catch{if($taskServer.HasExited){throw 'Maintenance API exited'};Start-Sleep -Milliseconds 100}}
 if(!$ready){throw 'Maintenance API failed to start'}
}
function Stop-Api {if($taskServer -and !$taskServer.HasExited){Stop-Process -Id $taskServer.Id -Force;$null=$taskServer.WaitForExit(5000)}}
function Schedule($at,[string]$reason,[long]$revision){
 $body=@{mode=$(if($at){'scheduled'}else{'running'});atUtc=$at;reason=$reason;expectedRevision=$revision}|ConvertTo-Json -Compress
 Invoke-RestMethod ($taskApi+'/api/operations/lobbies/sample-training/mode') -Method Post -Headers $taskOpsHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
try{
 foreach($key in $taskSettings.Keys){$taskOld[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$taskSettings[$key],'Process')}
 Start-Api
 $login=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"shutdown-admin"}';$taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 $room=(Api-Post '/api/Room/create' @{lobbyId='sample-training';roomName='shutdown-room';contentId='sample-training';maxUsers=2}).room.id
 for($n=0;$n -lt 3;$n++){
  $folder=Join-Path $taskRun $n.ToString();New-Item -ItemType Directory -Path $folder -Force|Out-Null
  $args='-batchmode -nographics --smarttraining-probe "'+$folder+'" -logFile "'+(Join-Path $folder 'player.log')+'"'
  $taskPlayers+=Start-Process $taskClientExe -ArgumentList $args -WorkingDirectory (Split-Path $taskClientExe -Parent) -WindowStyle Hidden -PassThru
  $null=Wait-Player $n {param($s) $s.processId -gt 0} 'Player startup'
  $null=Send-Player $n 'login' ('shutdown-player-'+$n) $taskApi;$null=Wait-Player $n {param($s) $s.loggedIn -and !$s.busy} 'Player login'
 }
 foreach($n in @(0,1)){$null=Send-Player $n 'lobby' 'sample-training';$null=Wait-Player $n {param($s) !$s.busy -and $s.lobbyId -eq 'sample-training'} 'Target member'}
 $null=Send-Player 0 'join' $room 'sample-training';$null=Wait-Player 0 {param($s) $s.connected -and !$s.busy} 'Room member'
 $null=Send-Player 2 'lobby' 'safety-training';$null=Wait-Player 2 {param($s) !$s.busy -and $s.lobbyId -eq 'safety-training'} 'Other lobby'
 $before=Ops;$pidLobby=(Root).processId;$pidRoom=$before.rooms[0].processId;$pidOther=($before.lobbies|Where-Object {$_.id -eq 'safety-training'}).processId
 $due=[DateTimeOffset]::UtcNow.AddSeconds(30).ToString('o');$set=Schedule $due '서버 업데이트' 0
 Check ($set.success -and !$set.persistenceWarning -and (Root).shutdown.state -eq 'scheduled') 'local schedule is persisted'
 foreach($n in @(0,1)){$null=Wait-Player $n {param($s) @($s.lobbies|Where-Object {$_.id -eq 'sample-training' -and $_.shutdownState -eq 'scheduled' -and $_.shutdownReason -eq '서버 업데이트'}).Count -eq 1} 'Automatic shutdown notice'}
 Check ((Read-Player 0).connected -and (Read-Player 1).lobbyId -eq 'sample-training') 'room and lobby-only clients receive notice while remaining connected'
 $outsider=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"shutdown-outsider"}';$outsideAuth=@{Authorization=('Bearer '+$outsider.accessToken)}
 $blocked=$false;try{$null=Invoke-RestMethod ($taskApi+'/api/Lobby/join') -Method Post -Headers $outsideAuth -ContentType application/json -Body '{"lobbyId":"sample-training"}'}catch{$blocked=$_.Exception.Response.StatusCode.value__ -eq 400}
 Check ($blocked -and (Root).pendingUsers -eq 0) 'scheduled lobby rejects fresh admission without reservation'
 $stale=$false;try{$null=Schedule $null 'stale' 0}catch{$stale=$_.Exception.Response.StatusCode.value__ -eq 409};Check $stale 'stale operator cancellation is rejected'
 $invalid=$false;try{$null=Schedule ([DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')) 'past' 1}catch{$invalid=$_.Exception.Response.StatusCode.value__ -eq 400};Check $invalid 'past deadline is rejected'
 $forbidden=$false;try{$null=Api-Post '/api/operations/lobbies/sample-training/mode' @{mode='running';atUtc=$null;reason='user';expectedRevision=1}}catch{$forbidden=$_.Exception.Response.StatusCode.value__ -eq 403};Check $forbidden 'ordinary user request cannot cancel operations schedule'
 $evil=@{'X-Operations-Request'='1';Origin='https://attacker.example'};$forbidden=$false
 try{$null=Invoke-RestMethod ($taskApi+'/api/operations/lobbies/sample-training/mode') -Method Post -Headers $evil -ContentType application/json -Body '{"mode":"running","atUtc":null,"reason":"cross","expectedRevision":1}'}catch{$forbidden=$_.Exception.Response.StatusCode.value__ -eq 403};Check $forbidden 'cross-origin cancellation is rejected'
 $null=Schedule $null '예약 취소' 1
 $null=Wait-Player 1 {param($s) @($s.lobbies|Where-Object {$_.id -eq 'sample-training' -and $_.shutdownState -eq 'none'}).Count -eq 1} 'Automatic cancellation notice'
 Check ((Read-Player 0).connected -and (Root).processId -eq $pidLobby -and (Ops).rooms[0].processId -eq $pidRoom) 'cancellation retains worker identities and active room'
 $null=Invoke-RestMethod ($taskApi+'/api/Lobby/join') -Method Post -Headers $outsideAuth -ContentType application/json -Body '{"lobbyId":"sample-training"}'
 $null=Invoke-RestMethod ($taskApi+'/api/Lobby/leave') -Method Post -Headers $outsideAuth -ContentType application/json -Body '{"lobbyId":"sample-training"}'
 Check ((Root).connectedUsers -eq 3) 'cancellation reopens admission'
 $due=[DateTimeOffset]::UtcNow.AddSeconds(8).ToString('o');$null=Schedule $due '예약 종료 검증' 2
 $null=Wait-Player 0 {param($s) !$s.connected -and !$s.busy -and $s.lobbyId -eq '' -and $s.roomId -eq '' -and $s.reconnectRoomId -eq ''} 'Room member returned to list'
 Check ((Read-Player 0).loggedIn) 'room participant returns to lobby list without losing login or offering stale reconnect'
 $null=Wait-Player 1 {param($s) !$s.busy -and $s.lobbyId -eq ''} 'Lobby-only member returned to list'
 Check ((Read-Player 1).loggedIn) 'lobby-only participant automatically returns to list'
 Check ((Root).operation.mode -eq 'maintenance' -and (Root).maintenance.enabled) 'scheduled deadline transitions into unified maintenance'
 $status=Root
 Check ($status.shutdown.state -eq 'completed' -and !$status.processRunning -and $status.connectedUsers -eq 0 -and $status.pendingUsers -eq 0 -and @((Ops).rooms).Count -eq 0) 'deadline empties target membership and stops all target workers'
 Check ((Read-Player 2).lobbyId -eq 'safety-training' -and ((Ops).lobbies|Where-Object {$_.id -eq 'safety-training'}).processId -eq $pidOther) 'other lobby stays connected to same worker'
 $blocked=$false;try{$null=Invoke-RestMethod ($taskApi+'/api/Lobby/join') -Method Post -Headers $outsideAuth -ContentType application/json -Body '{"lobbyId":"sample-training"}'}catch{$blocked=$_.Exception.Response.StatusCode.value__ -eq 400};Check $blocked 'completed closure stays blocked until explicit release'
 $null=Schedule $null '운영 재개' 4
 $null=Send-Player 1 'lobby' 'sample-training';$null=Wait-Player 1 {param($s) !$s.busy -and $s.lobbyId -eq 'sample-training'} 'Reopen on demand'
 Check ((Root).processRunning) 'release permits on-demand restart'
 Check ((Root).operation.mode -eq 'running' -and !(Root).maintenance.enabled -and (Root).shutdown.state -eq 'none') 'one resume removes both admission block and deadline'
 foreach($n in @(0,1,2)){$null=Send-Player $n 'logout';$null=Wait-Player $n {param($s) !$s.loggedIn -and !$s.busy} 'Final logout'}
 $null=Api-Post '/api/Auth/logout' @{}
 $due=[DateTimeOffset]::UtcNow.AddSeconds(6).ToString('o');$null=Schedule $due '재시작 예약' 5
 Stop-Api;Start-Api
 Check ((Root).shutdown.state -eq 'scheduled' -and (Root).shutdown.reason -eq '재시작 예약' -and !(Root).processRunning) 'ASP.NET restart restores scheduled deadline without launching worker'
 $end=[DateTime]::UtcNow.AddSeconds(15);do{$status=Root;if($status.shutdown.state -eq 'completed'){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $end)
 Check ($status.shutdown.state -eq 'completed' -and !$status.processRunning) 'restored deadline completes without users or worker resurrection'
 Stop-Api;Start-Api
 Check ((Root).shutdown.state -eq 'completed') 'completed block persists across another restart'
 $events=(Ops).recentEvents
 Check (@($events|Where-Object {$_.action -eq 'shutdown_completed'}).Count -eq 2 -and @($events|Where-Object {$_.action -eq 'failed' -and $_.reason -eq '로비 종료 예약 실행'}).Count -eq 0) 'planned closures record reason and time once without false failures'
 Write-Output ('SHUTDOWN COMPLETE: '+$taskRun)
}finally{
 foreach($player in $taskPlayers){if(!$player.HasExited){Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue}}
 Stop-Api
 foreach($key in $taskOld.Keys){[Environment]::SetEnvironmentVariable($key,$taskOld[$key],'Process')}
}
