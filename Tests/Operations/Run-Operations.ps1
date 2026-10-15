param([string]$ServerProject,[string]$ClientExecutable,[string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskServerRoot=Join-Path $taskRoot 'SmartTrainingServer'
$taskClientExe=Join-Path $taskRoot 'UnityClient/Builds/Client/Smart Training Client.exe'
$taskRun=Join-Path $taskRoot ('operations-evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if($ServerProject){$taskServerRoot=[IO.Path]::GetFullPath($ServerProject)}
if($ClientExecutable){$taskClientExe=[IO.Path]::GetFullPath($ClientExecutable)}
if($EvidenceRoot){$taskRun=Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) (Get-Date -Format 'yyyyMMdd-HHmmss')}
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskApi='http://127.0.0.1:51440'
$taskServer=$null; $taskPlayers=@(); $taskIds=@(0,0,0); $taskOld=@{}
$taskSettings=@{ASPNETCORE_ENVIRONMENT='Development'; Server__Port='51440'; Room__ServerApiBaseUrl=$taskApi; Room__AdvertisedAddress='127.0.0.1'; SMART_TRAINING_SERVER_PROBE='1'}
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
function Status {Invoke-RestMethod ($taskApi+'/api/operations')}
function Wait-Status([scriptblock]$condition,[string]$name){
 $end=[DateTime]::UtcNow.AddSeconds(45)
 do{$value=Status;if(& $condition $value){return $value};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $end)
 throw ('Operations timeout: '+$name)
}
function Post-As([string]$path,$body,$auth){Invoke-RestMethod ($taskApi+$path) -Method Post -Headers $auth -ContentType 'application/json' -Body ($body|ConvertTo-Json -Compress)}
function Owned-Worker([string]$id,[bool]$lobby){
 $matches=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId = '+$taskServer.Id) | Where-Object {$_.CommandLine -match ('-Id '+[regex]::Escape($id)+' ') -and ($_.CommandLine.Contains(' -lobby ') -eq $lobby)})
 if($matches.Count -eq 1){return $matches[0]};return $null
}
try{
 foreach($key in $taskSettings.Keys){$taskOld[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$taskSettings[$key],'Process')}
 $dll=Join-Path $taskServerRoot 'bin/Debug/net8.0/Smart Training Server.dll'
 $taskServer=Start-Process 'dotnet' -ArgumentList ('"'+$dll+'"') -WorkingDirectory $taskServerRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun 'server.log') -RedirectStandardError (Join-Path $taskRun 'errors.log')
 $ready=$false
 for($n=0;$n -lt 60;$n++){try{$null=Invoke-RestMethod ($taskApi+'/api/health');$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
 if(!$ready){throw 'Operations test API failed to start'}
 $initial=Status
 Check (@($initial.lobbies).Count -eq 3 -and @($initial.rooms).Count -eq 0 -and @($initial.lobbies|Where-Object {$_.processRunning}).Count -eq 0) 'local operations API reports three idle catalog lobbies without launching workers'
 $page=Invoke-WebRequest ($taskApi+'/operations') -UseBasicParsing
 Check ($page.StatusCode -eq 200 -and $page.Content.Contains('/api/operations') -and $page.Content.Contains('setInterval(refresh,5000)') -and $page.Headers['Cache-Control'] -eq 'no-store') 'local dashboard is served with automatic refresh and no caching'
 Add-Type -AssemblyName System.Net.Http
 $handler=New-Object System.Net.Http.HttpClientHandler;$handler.UseProxy=$false
 $guardClient=New-Object System.Net.Http.HttpClient($handler)
 try{
  $guardClient.DefaultRequestHeaders.Host='attacker.example:51440'
  $response=$guardClient.GetAsync(($taskApi+'/api/operations')).GetAwaiter().GetResult()
  Check ([int]$response.StatusCode -eq 403) 'nonlocal Host cannot read operations over loopback connection'
  $guardClient.DefaultRequestHeaders.Host='127.0.0.1:51440'
  $null=$guardClient.DefaultRequestHeaders.TryAddWithoutValidation('Origin','https://attacker.example')
  $response=$guardClient.GetAsync(($taskApi+'/api/operations')).GetAwaiter().GetResult()
  Check ([int]$response.StatusCode -eq 403) 'cross-origin browser request is rejected over real HTTP'
 }finally{$guardClient.Dispose()}
 $login=Post-As '/api/Auth/login' @{userName='ops-admin'} @{}
 $taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 $started=Status;$lobby=$started.lobbies|Where-Object {$_.id -eq 'sample-training'}
 $worker=Owned-Worker 'sample-training' $true
 Check ($lobby.connectedUsers -eq 1 -and $lobby.processRunning -and $lobby.processId -eq $worker.ProcessId -and $lobby.lifecycle.startedAtUtc -and $lobby.lifecycle.state -eq 'running') 'live lobby row matches actual worker PID readiness and membership'
 $room=(Api-Post '/api/Room/create' @{lobbyId='sample-training';roomName='ops-native-room';contentId='sample-training';maxUsers=2}).room.id
 $before=Status
 Check ($before.rooms[0].id -eq $room -and !$before.rooms[0].processRunning -and !$before.rooms[0].lifecycle) 'unoccupied room appears without a process start record'
 $folder=Join-Path $taskRun '0';New-Item -ItemType Directory -Path $folder -Force | Out-Null
 $args='-batchmode -nographics --smarttraining-probe "'+$folder+'" -logFile "'+(Join-Path $folder 'player.log')+'"'
 $taskPlayers+=Start-Process $taskClientExe -ArgumentList $args -WorkingDirectory (Split-Path $taskClientExe -Parent) -WindowStyle Hidden -PassThru
 $null=Wait-Player 0 {param($s) $s.processId -gt 0} 'Player startup'
 $null=Send-Player 0 'login' 'ops-player' $taskApi
 $null=Wait-Player 0 {param($s) $s.loggedIn -and !$s.busy} 'Player login'
 $null=Send-Player 0 'lobby' 'sample-training'
 $null=Wait-Player 0 {param($s) !$s.busy -and $s.lobbyId -eq 'sample-training'} 'Player lobby entry'
 $null=Send-Player 0 'join' $room 'sample-training'
 $player=Wait-Player 0 {param($s) $s.connected -and !$s.busy} 'Player room connection'
 $null=Api-Post '/api/Auth/logout' @{}
 $active=Status;$roomRow=$active.rooms[0];$roomWorker=Owned-Worker $room $false
 Check ($roomRow.connectedUsers -eq 1 -and $roomRow.pendingUsers -eq 0 -and $roomRow.processRunning -and $roomRow.processId -eq $roomWorker.ProcessId -and $roomRow.lifecycle.startedAtUtc) 'operations room count and PID match actual Netcode participant and worker'
 $pendingLogin=Post-As '/api/Auth/login' @{userName='ops-pending'} @{}
 $pendingAuth=@{Authorization=('Bearer '+$pendingLogin.accessToken)}
 $null=Post-As '/api/Lobby/join' @{lobbyId='sample-training'} $pendingAuth
 $reservation=Post-As '/api/Room/join' @{lobbyId='sample-training';roomId=$room} $pendingAuth
 $pending=Status
 Check ($pending.rooms[0].connectedUsers -eq 1 -and $pending.rooms[0].pendingUsers -eq 1) 'operations snapshot distinguishes Netcode reservation from confirmed connection'
 $raw=Invoke-WebRequest ($taskApi+'/api/operations') -UseBasicParsing
 Check (!$raw.Content.Contains($pendingLogin.accessToken) -and !$raw.Content.Contains($reservation.admissionToken) -and !$raw.Content.Contains($player.userId)) 'operations JSON excludes user authentication and admission tokens and participant identities'
 $events=@($pending.recentEvents).Count;$unchanged=Status
 Check (@($unchanged.recentEvents).Count -eq $events -and $unchanged.rooms[0].pendingUsers -eq 1) 'status reads preserve admission and event history'
 $null=Post-As '/api/Room/leave' @{lobbyId='sample-training';roomId=$room} $pendingAuth
 $null=Post-As '/api/Auth/logout' @{} $pendingAuth
 Stop-Process -Id $roomWorker.ProcessId -Force
 $null=Wait-Player 0 {param($s) !$s.connected -and !$s.busy -and $s.reconnectRoomId -ne ''} 'Room crash cleanup'
 $failed=Wait-Status {param($s) $s.rooms[0].lifecycle.failedAtUtc -and !$s.rooms[0].processRunning} 'Failed room record'
 Check ($failed.rooms[0].connectedUsers -eq 0 -and $failed.rooms[0].lifecycle.stoppedAtUtc -and $failed.rooms[0].lifecycle.lastFailure.Contains('종료')) 'unexpected worker shutdown records failure and stop time'
 $null=Send-Player 0 'reconnect'
 $null=Wait-Player 0 {param($s) $s.connected -and !$s.busy} 'Room restart'
 $restarted=Status
 Check ($restarted.rooms[0].lifecycle.startCount -eq 2 -and $restarted.rooms[0].lifecycle.lastFailure -and $restarted.rooms[0].processId -ne $roomWorker.ProcessId) 'successful restart increments attempts and preserves last failure'
 $roomPid=$restarted.rooms[0].processId
 $lobbyWorker=Owned-Worker 'sample-training' $true
 Stop-Process -Id $lobbyWorker.ProcessId -Force
 $recovered=Wait-Status {param($s) @($s.lobbies|Where-Object {$_.id -eq 'sample-training' -and $_.lifecycle.startCount -eq 2 -and $_.processRunning -and $_.state -eq 'running'}).Count -eq 1} 'Lobby recovery'
 $local=$recovered.lobbies|Where-Object {$_.id -eq 'sample-training'}
 Check ($local.lifecycle.failedAtUtc -and $local.lifecycle.lastFailure -and $local.processId -ne $lobbyWorker.ProcessId -and $recovered.rooms[0].processId -eq $roomPid) 'lobby restart records its own failure while keeping room worker unchanged'
 $null=Send-Player 0 'logout'
 $null=Wait-Player 0 {param($s) !$s.loggedIn -and !$s.connected -and !$s.busy} 'Final logout'
 $ended=Status
 Check (@($ended.rooms).Count -eq 0 -and @($ended.recentProcesses|Where-Object {$_.id -eq $room -and $_.lastFailure -and $_.stoppedAtUtc}).Count -eq 1) 'deleted room retains its historical failure and shutdown record'
 Check (@($ended.lobbies|Where-Object {$_.processRunning -or $_.connectedUsers -ne 0 -or $_.pendingUsers -ne 0}).Count -eq 0 -and @($ended.lobbies).Count -eq 3) 'final operations snapshot lists three empty lobbies and no active workers'
 $ended|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $taskRun 'final-operations.json') -Encoding UTF8
 Write-Output ('OPERATIONS COMPLETE: '+$taskRun)
}finally{
 foreach($player in $taskPlayers){if(!$player.HasExited){Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue}}
 if($taskServer -and !$taskServer.HasExited){Stop-Process -Id $taskServer.Id -Force -ErrorAction SilentlyContinue}
 foreach($key in $taskOld.Keys){[Environment]::SetEnvironmentVariable($key,$taskOld[$key],'Process')}
}
