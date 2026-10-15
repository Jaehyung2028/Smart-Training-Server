param([string]$ServerProject,[string]$ClientExecutable,[string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskServerRoot=Join-Path $taskRoot 'SmartTrainingServer'
$taskClientExe=Join-Path $taskRoot 'UnityClient/Builds/Client/Smart Training Client.exe'
$taskRun=Join-Path $taskRoot ('maintenance-evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if($ServerProject){$taskServerRoot=[IO.Path]::GetFullPath($ServerProject)}
if($ClientExecutable){$taskClientExe=[IO.Path]::GetFullPath($ClientExecutable)}
if($EvidenceRoot){$taskRun=Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) (Get-Date -Format 'yyyyMMdd-HHmmss')}
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskApi='http://127.0.0.1:51450'
$taskServer=$null; $taskPlayers=@(); $taskIds=@(0,0,0); $taskOld=@{}
$taskSettings=@{ASPNETCORE_ENVIRONMENT='Development'; Server__Port='51450'; Room__ServerApiBaseUrl=$taskApi; Room__AdvertisedAddress='127.0.0.1'; SMART_TRAINING_SERVER_PROBE='1'}
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
function Mode([bool]$enabled,[string]$reason,[long]$revision){Invoke-RestMethod ($taskApi+'/api/operations/lobbies/sample-training/maintenance') -Method Post -Headers $taskOpsHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{enabled=$enabled;reason=$reason;expectedRevision=$revision}|ConvertTo-Json -Compress)))}
function Root { (Ops).lobbies|Where-Object {$_.id -eq 'sample-training'} }
function Start-Api {
 $script:taskRound++;$dll=Join-Path $taskServerRoot 'bin/Debug/net8.0/Smart Training Server.dll'
 $script:taskServer=Start-Process dotnet -ArgumentList ('"'+$dll+'"') -WorkingDirectory $taskServerRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun ('server-'+$taskRound+'.log')) -RedirectStandardError (Join-Path $taskRun ('errors-'+$taskRound+'.log'))
 $ready=$false
 for($n=0;$n -lt 100;$n++){try{$null=Invoke-RestMethod ($taskApi+'/api/health');$ready=$true;break}catch{if($taskServer.HasExited){throw 'Maintenance API exited'};Start-Sleep -Milliseconds 100}}
 if(!$ready){throw 'Maintenance API failed to start'}
}
function Stop-Api {if($taskServer -and !$taskServer.HasExited){Stop-Process -Id $taskServer.Id -Force;$null=$taskServer.WaitForExit(5000)}}
try{
 foreach($key in $taskSettings.Keys){$taskOld[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$taskSettings[$key],'Process')}
 Start-Api
 $login=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"maintenance-admin"}'
 $taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 $room=(Api-Post '/api/Room/create' @{lobbyId='sample-training';roomName='maintenance-room';contentId='sample-training';maxUsers=2}).room.id
 for($n=0;$n -lt 3;$n++){
  $folder=Join-Path $taskRun $n.ToString();New-Item -ItemType Directory -Path $folder -Force|Out-Null
  $args='-batchmode -nographics --smarttraining-probe "'+$folder+'" -logFile "'+(Join-Path $folder 'player.log')+'"'
  $taskPlayers+=Start-Process $taskClientExe -ArgumentList $args -WorkingDirectory (Split-Path $taskClientExe -Parent) -WindowStyle Hidden -PassThru
  $null=Wait-Player $n {param($s) $s.processId -gt 0} 'Player startup'
  $null=Send-Player $n 'login' ('maintenance-player-'+$n) $taskApi
  $null=Wait-Player $n {param($s) $s.loggedIn -and !$s.busy} 'Player login'
 }
 $null=Send-Player 0 'lobby' 'sample-training';$null=Wait-Player 0 {param($s) !$s.busy -and $s.lobbyId -eq 'sample-training'} 'Existing lobby member'
 $null=Send-Player 0 'join' $room 'sample-training';$null=Wait-Player 0 {param($s) $s.connected -and !$s.busy} 'Existing room member'
 $before=Ops;$lobbyPid=(Root).processId;$roomPid=$before.rooms[0].processId
 $on=Mode $true '멀티 접속 점검' 0
 Check ($on.success -and !$on.persistenceWarning -and $on.maintenance.revision -eq 1) 'local operator enables and persists maintenance'
 $status=Root
 Check ($status.maintenance.enabled -and $status.processId -eq $lobbyPid -and (Ops).rooms[0].processId -eq $roomPid -and $status.lifecycle.state -eq 'running') 'maintenance preserves existing lobby and room worker identities and lifecycle'
 $notice=Wait-Player 1 {param($s) @($s.lobbies|Where-Object {$_.id -eq 'sample-training' -and $_.isMaintenance -and $_.maintenanceReason -eq '멀티 접속 점검'}).Count -eq 1} 'Unity automatic maintenance notice'
 Check $notice.loggedIn 'outsider Unity lobby list automatically displays maintenance and reason'
 $null=Send-Player 1 'lobby' 'sample-training'
 $id=$taskIds[1];$blocked=Wait-Player 1 {param($s) $s.completed -ge $id -and !$s.busy -and $s.status.Contains('점검 중')} 'Server authoritative admission rejection'
 Check ($blocked.loggedIn -and $blocked.lobbyId -eq '' -and $blocked.pendingLobbyId -eq '' -and (Root).pendingUsers -eq 0) 'server rejects new admission without pending reservation even when client bypasses disabled button'
 $null=Send-Player 2 'lobby' 'safety-training'
 $null=Wait-Player 2 {param($s) !$s.busy -and $s.lobbyId -eq 'safety-training'} 'Unaffected lobby entry'
 Check $true 'another catalog lobby remains open for admission'
 $null=Send-Player 0 'ready' '' '' $true;$null=Wait-Player 0 {param($s) $s.allReady -and !$s.busy} 'Ready during maintenance'
 $null=Send-Player 0 'phase' '' '' $true;$null=Wait-Player 0 {param($s) $s.running -and !$s.busy} 'Running during maintenance'
 Check ((Ops).rooms[0].trainingRunning) 'existing room still supports ready and running state during maintenance'
 $null=Send-Player 0 'phase' '' '' $false;$null=Wait-Player 0 {param($s) !$s.running -and !$s.busy} 'Return to waiting'
 $eventCount=@((Ops).recentEvents|Where-Object {$_.action -eq 'maintenance_on'}).Count
 $same=Mode $true '멀티 접속 점검' 0
 Check ($same.success -and $same.maintenance.revision -eq 1 -and @((Ops).recentEvents|Where-Object {$_.action -eq 'maintenance_on'}).Count -eq $eventCount) 'duplicate operator request does not repeat state change or event'
 $stale=$false;try{$null=Mode $false '오래된 요청' 0}catch{$stale=$_.Exception.Response.StatusCode.value__ -eq 409}
 Check ($stale -and (Root).maintenance.enabled) 'stale revision cannot undo current maintenance'
 $invalid=$false;try{$null=Mode $false ' ' 1}catch{$invalid=$_.Exception.Response.StatusCode.value__ -eq 400}
 Check $invalid 'operator must supply meaningful reason'
 $forbidden=$false;try{$null=Api-Post '/api/operations/lobbies/sample-training/maintenance' @{enabled=$false;reason='일반 요청';expectedRevision=1}}catch{$forbidden=$_.Exception.Response.StatusCode.value__ -eq 403}
 Check $forbidden 'ordinary authenticated API request cannot mutate maintenance without management header'
 $evil=@{'X-Operations-Request'='1';Origin='https://attacker.example'};$forbidden=$false
 try{$null=Invoke-RestMethod ($taskApi+'/api/operations/lobbies/sample-training/maintenance') -Method Post -Headers $evil -ContentType application/json -Body '{"enabled":false,"reason":"cross-origin","expectedRevision":1}'}catch{$forbidden=$_.Exception.Response.StatusCode.value__ -eq 403}
 Check $forbidden 'cross-origin management mutation is rejected'
 $off=Mode $false '접속 재개' 1
 $null=Wait-Player 1 {param($s) @($s.lobbies|Where-Object {$_.id -eq 'sample-training' -and !$_.isMaintenance}).Count -eq 1} 'Unity automatic release notice'
 $null=Send-Player 1 'retryLobby'
 $null=Wait-Player 1 {param($s) !$s.busy -and $s.lobbyId -eq 'sample-training'} 'Admission after release'
 $null=Send-Player 1 'join' $room 'sample-training'
 $null=Wait-Player 1 {param($s) $s.connected -and !$s.busy -and @($s.participants).Count -eq 2} 'Room after release'
 Check ($off.maintenance.revision -eq 2 -and (Ops).rooms[0].processId -eq $roomPid) 'release permits previously blocked client to join original room without replacing server'
 $null=Mode $true '재점검' 2
 $null=Wait-Player 0 {param($s) @($s.participants).Count -eq 2} 'Two existing participants'
 $null=Send-Player 0 'disconnect'
 $null=Wait-Player 0 {param($s) !$s.connected -and !$s.busy -and $s.reconnectRoomId -ne ''} 'Existing member transport loss'
 $null=Send-Player 0 'reconnect'
 $null=Wait-Player 0 {param($s) $s.connected -and !$s.busy -and @($s.participants).Count -eq 2} 'Existing lobby member reconnect'
 Check ((Root).maintenance.enabled -and (Ops).rooms[0].processId -eq $roomPid) 'existing lobby member can reconnect to room during maintenance'
 foreach($n in @(0,1,2)){$null=Send-Player $n 'logout';$null=Wait-Player $n {param($s) !$s.loggedIn -and !$s.connected -and !$s.busy} 'Final participant logout'}
 $null=Api-Post '/api/Auth/logout' @{}
 Check ((Root).maintenance.enabled -and !(Root).processRunning -and (Root).connectedUsers -eq 0) 'last departure stops worker while keeping maintenance enabled'
 $deadline=[DateTime]::UtcNow.AddSeconds(8)
 do{$saved=Get-Content -LiteralPath $taskSettings['Operations__HistoryPath'] -Raw -Encoding UTF8|ConvertFrom-Json;if(@($saved.Processes|Where-Object {$_.State -in @('starting','running')}).Count -eq 0){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
 Stop-Api;Start-Api
 $restored=Root
 Check ($restored.maintenance.enabled -and $restored.maintenance.reason -eq '재점검' -and $restored.maintenance.revision -eq 3 -and !$restored.processRunning) 'actual ASP.NET restart restores maintenance without launching a worker'
 $login=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"maintenance-after"}';$taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $denied=$false;try{$null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}}catch{$denied=$_.Exception.Response.StatusCode.value__ -eq 400}
 Check ($denied -and !(Root).processRunning -and (Root).pendingUsers -eq 0) 'restored maintenance rejects new user without demand or process launch'
 $null=Mode $false '정상 운영' 3
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 Check ((Root).connectedUsers -eq 1 -and (Root).processRunning -and !(Root).maintenance.enabled) 'operator can release restored mode and start normal on-demand admission'
 $null=Api-Post '/api/Auth/logout' @{}
 $log=(Ops).recentEvents
 Check (@($log|Where-Object {$_.action -eq 'maintenance_on' -and $_.reason -eq '재점검' -and $_.atUtc}).Count -eq 1 -and @($log|Where-Object {$_.action -eq 'maintenance_off' -and $_.reason -eq '정상 운영'}).Count -eq 1) 'maintenance changes retain time and reason across host restart'
 Write-Output ('MAINTENANCE COMPLETE: '+$taskRun)
}finally{
 foreach($player in $taskPlayers){if(!$player.HasExited){Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue}}
 Stop-Api
 foreach($key in $taskOld.Keys){[Environment]::SetEnvironmentVariable($key,$taskOld[$key],'Process')}
}
