param([string]$ServerProject,[string]$ClientExecutable,[string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskServerRoot=Join-Path $taskRoot 'SmartTrainingServer'
$taskClientExe=Join-Path $taskRoot 'UnityClient/Builds/Client/Smart Training Client.exe'
$taskRun=Join-Path $taskRoot ('history-restart-evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if($ServerProject){$taskServerRoot=[IO.Path]::GetFullPath($ServerProject)}
if($ClientExecutable){$taskClientExe=[IO.Path]::GetFullPath($ClientExecutable)}
if($EvidenceRoot){$taskRun=Join-Path ([IO.Path]::GetFullPath($EvidenceRoot)) (Get-Date -Format 'yyyyMMdd-HHmmss')}
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskApi='http://127.0.0.1:51445'
$taskServer=$null; $taskPlayers=@(); $taskIds=@(0,0,0); $taskOld=@{}
$taskSettings=@{ASPNETCORE_ENVIRONMENT='Development'; Server__Port='51445'; Room__ServerApiBaseUrl=$taskApi; Room__AdvertisedAddress='127.0.0.1'; SMART_TRAINING_SERVER_PROBE='1'}
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
$taskSettings['Operations__OpenBrowserOnStart']='false'
$taskSettings['Operations__PersistHistory']='true'
$taskSettings['Operations__HistoryPath']=Join-Path $taskRun 'history.json'
$taskSettings['SMART_TRAINING_SERVER_PROBE']='0'
$taskHistory=$taskSettings['Operations__HistoryPath'];$taskRound=0
function Status {Invoke-RestMethod ($taskApi+'/api/operations')}
function Start-Api {
 $script:taskRound++
 $dll=Join-Path $taskServerRoot 'bin/Debug/net8.0/Smart Training Server.dll'
 $script:taskServer=Start-Process dotnet -ArgumentList ('"'+$dll+'"') -WorkingDirectory $taskServerRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun ('server-'+$taskRound+'.log')) -RedirectStandardError (Join-Path $taskRun ('errors-'+$taskRound+'.log'))
 $ready=$false
 for($n=0;$n -lt 100;$n++){try{$null=Invoke-RestMethod ($taskApi+'/api/health');$ready=$true;break}catch{if($taskServer.HasExited){throw 'Test API exited'};Start-Sleep -Milliseconds 100}}
 if(!$ready){throw 'History test API did not start'}
}
function Stop-Api {
 if($taskServer -and !$taskServer.HasExited){Stop-Process -Id $taskServer.Id -Force; $null=$taskServer.WaitForExit(5000)}
}
function Wait-Saved([scriptblock]$condition){
 $end=[DateTime]::UtcNow.AddSeconds(15)
 do{
  try{$saved=Get-Content -LiteralPath $taskHistory -Raw -Encoding UTF8|ConvertFrom-Json;if(& $condition $saved){return $saved}}catch{}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $end)
 throw 'History file condition timed out'
}
try{
 foreach($key in $taskSettings.Keys){$taskOld[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$taskSettings[$key],'Process')}
 Start-Api
 $initial=Status
 Check ($initial.history.enabled -and @($initial.recentEvents).Count -eq 0) 'production host enables isolated persistent history with initially empty records'
 $login=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"history-first"}'
 $taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 $live=Status;$firstLobby=$live.lobbies|Where-Object {$_.id -eq 'sample-training'}
 $oldWorker=$firstLobby.processId
 Check ($firstLobby.processRunning -and $firstLobby.lifecycle.startCount -eq 1) 'real lobby startup is reflected in live operations'
 $saved=Wait-Saved {param($s) @($s.Processes|Where-Object {$_.Id -eq 'sample-training' -and $_.State -eq 'running'}).Count -eq 1}
 Check (@($saved.Events|Where-Object {$_.Action -eq 'ready'}).Count -eq 1) 'background writer saves actual readiness without a shutdown'
 $raw=Get-Content -LiteralPath $taskHistory -Raw -Encoding UTF8
 Check (!$raw.Contains($login.accessToken) -and !$raw.Contains($login.user.id)) 'persisted operational history excludes authentication and user identity'
 Stop-Api
 Start-Api
 $restored=Status;$lobby=$restored.lobbies|Where-Object {$_.id -eq 'sample-training'}
 Check (!$lobby.processRunning -and $lobby.connectedUsers -eq 0 -and $lobby.state -eq 'offline' -and $lobby.lifecycle.startCount -eq 1) 'restart restores historical lifecycle without starting old lobby or restoring old users'
 Check ($lobby.lifecycle.failedAtUtc -and $lobby.lifecycle.lastFailure.Contains('정상 종료되지') -and @($restored.recentEvents|Where-Object {$_.action -eq 'interrupted'}).Count -eq 1) 'abruptly stopped host produces explicit prior-session interruption record'
 Check (@(Get-Process -Id $oldWorker -ErrorAction SilentlyContinue).Count -eq 0) 'prior owned Unity worker does not remain after forced API termination'
 $login=Invoke-RestMethod ($taskApi+'/api/Auth/login') -Method Post -ContentType application/json -Body '{"userName":"history-second"}'
 $taskAuth=@{Authorization=('Bearer '+$login.accessToken)}
 $null=Api-Post '/api/Lobby/join' @{lobbyId='sample-training'}
 $again=Status;$lobby=$again.lobbies|Where-Object {$_.id -eq 'sample-training'}
 Check ($lobby.processRunning -and $lobby.lifecycle.startCount -eq 2 -and $lobby.lifecycle.lastFailure) 'fresh join increments persisted attempts and preserves previous failure'
 $null=Api-Post '/api/Auth/logout' @{}
 $null=Wait-Saved {param($s) @($s.Processes|Where-Object {$_.Id -eq 'sample-training' -and $_.State -eq 'offline' -and $_.StartCount -eq 2}).Count -eq 1}
 Stop-Api;Start-Api
 $third=Status;$lobby=$third.lobbies|Where-Object {$_.id -eq 'sample-training'}
 Check ($lobby.lifecycle.startCount -eq 2 -and @($third.recentEvents|Where-Object {$_.action -eq 'interrupted'}).Count -eq 1) 'stopped history survives another restart without a false additional crash'
 $page=Invoke-WebRequest ($taskApi+'/operations') -UseBasicParsing
 Check ($page.Content.Contains('id="history"') -and $page.Content.Contains('data.history.savedAtUtc')) 'operations page exposes persistence status and last saved time'
 Stop-Api
 [IO.File]::WriteAllText($taskHistory,'{broken fixture')
 Start-Api
 $repaired=Status
 Check (@(Get-ChildItem -LiteralPath $taskRun -Filter 'history.json.invalid-*').Count -eq 1 -and @($repaired.lobbies).Count -eq 3 -and @($repaired.recentEvents).Count -eq 0) 'corrupt history is preserved and host still starts with current catalog'
 $null=Wait-Saved {param($s) $s.Version -eq 1}
 Check $true 'new valid history is written after corrupt-file recovery'
 Write-Output ('OPERATIONS HISTORY COMPLETE: '+$taskRun)
}finally{
 Stop-Api
 foreach($key in $taskOld.Keys){[Environment]::SetEnvironmentVariable($key,$taskOld[$key],'Process')}
}
