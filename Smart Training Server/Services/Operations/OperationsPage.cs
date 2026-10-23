// 운영 페이지의 HTML·CSS·JavaScript를 문자열로 보관합니다. OperationsController.Page가 이 문자열을 웹 페이지로 응답합니다.
namespace Smart_Training_Server.Services.Operations
{
    internal static class OperationsPage
    {
        // 화면 구성: 상단 요약 → 로비 목록 → 운영 모드 입력 → 방 목록 → 최근 이벤트 표입니다.
        // 문자열 안의 table()은 textContent로 표를 만들고, openMode()는 설정 입력을 엽니다.
        // refresh()는 /api/operations를 조회해 표를 갱신하고 마지막 setInterval이 5초마다 호출합니다.
        // 적용 버튼은 /mode에 사유·시각·현재 버전을 POST합니다. 409이면 최신 설정이 달라졌으므로 다시 조회합니다.
        internal const string Html = """
<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Smart Training 서버 운영 상태</title>
<style>
body{font-family:system-ui,sans-serif;margin:0;background:#f3f5f8;color:#1b2738}main{max-width:1500px;margin:auto;padding:28px}
h1{font-size:26px;margin:0 0 8px}h2{font-size:19px;margin-top:30px}p{color:#4d5d70;line-height:1.6}
button{background:#174db8;color:white;border:0;border-radius:6px;padding:10px 18px;cursor:pointer}button:disabled{opacity:.5}
.table{overflow:auto;background:white;border:1px solid #d8dfeb;border-radius:8px}table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:12px;border-bottom:1px solid #e4e9f1;white-space:nowrap}th{background:#eaf0fa}td:last-child{white-space:normal;min-width:160px}
#message{min-height:26px}#summary{font-weight:600;color:#174db8}.note{font-size:13px}code{overflow-wrap:anywhere}
</style></head><body><main>
<h1>Smart Training 서버 운영 상태</h1>
<p>서버 PC 전용 운영 화면 · 5초마다 자동 갱신합니다. 점검 설정에서 입장만 차단 또는 예약 종료를 선택합니다. 운영 재개 한 번으로 해제합니다.</p>
<button id="refresh">지금 새로고침</button><p id="message" role="status">상태를 불러오고 있습니다…</p><p id="summary"></p><p id="history"></p>
<h2>콘텐츠 로비</h2><div class="table"><table><thead><tr><th>로비 이름 / ID</th><th>운영 모드 / 사유</th><th>사용자</th><th>방</th><th>운영 관리</th></tr></thead><tbody id="lobbies"></tbody></table></div>
<section id="mode-form" hidden><h2 id="mode-title"></h2>
<p>입장만 차단하면 기존 참가자는 계속 이용합니다. 예약 종료는 새 입장을 차단하고 지정 시각에 참가자를 목록으로 이동시킨 뒤 방 서버를 종료하고 로비 소속을 해제합니다. 종료 후에는 점검 중으로 유지합니다. 운영 재개는 모든 제한과 예약을 한 번에 해제합니다.</p>
<div id="mode-choice"><label for="mode-kind">점검 방식</label><select id="mode-kind"><option value="maintenance">입장만 차단</option><option value="scheduled">예약 종료</option></select></div>
<div id="mode-time-row" hidden><label for="mode-time">종료 시각 (현지 시각, 30일 이내)</label><input id="mode-time" type="datetime-local" step="1"></div>
<label for="mode-reason">변경 사유 (필수, 200자 이내)</label><br><textarea id="mode-reason" maxlength="200" rows="3" style="width:min(95%,650px)"></textarea><br><button id="mode-apply">설정 적용</button> <button id="mode-cancel">입력 닫기</button></section><p id="mode-result" role="status"></p>

<h2>현재 방</h2><div class="table"><table><thead><tr><th>방 이름 / ID</th><th>소속 로비</th><th>서버 / 훈련 상태</th><th>프로그램 / PID</th><th>접속 / 정원</th><th>예약</th><th>마지막 실행</th><th>마지막 종료</th><th>마지막 실패</th></tr></thead><tbody id="rooms"></tbody></table></div>
<h2>최근 실행·종료·실패 기록</h2><div class="table"><table><thead><tr><th>시각</th><th>종류</th><th>로비 ID</th><th>이름 / ID</th><th>구분</th><th>사유</th></tr></thead><tbody id="events"></tbody></table></div>
<p class="note">시각은 이 브라우저의 시간대로 표시합니다. 최근 기록 200건과 종료된 프로그램의 최근 상태 최대 512개를 보관합니다. 파일 저장이 켜져 있으면 서버 재시작 후에도 이력을 불러옵니다. 마지막 실행 시각은 실행 시도 시각입니다.</p>
</main><script>
const labels={offline:'꺼짐',creating:'실행 대기',starting:'준비 중',running:'준비 완료',stopped:'종료',stopping:'종료 중',failed:'실패',ready:'준비 완료',interrupted:'이전 실행 중단'};
const time=value=>value?new Date(value).toLocaleString('ko-KR'):'—';
const state=value=>value==='maintenance_on'?'점검 설정':value==='maintenance_off'?'운영 재개':labels[value]||value;
function table(id,rows,columns){const body=document.getElementById(id);body.replaceChildren();if(!rows.length){const row=document.createElement('tr'),cell=document.createElement('td');cell.colSpan=columns.length;cell.textContent='표시할 항목이 없습니다.';row.appendChild(cell);body.appendChild(row);return;}for(const item of rows){const row=document.createElement('tr');for(const column of columns){const cell=document.createElement('td');cell.textContent=String(column(item)??'—');row.appendChild(cell);}body.appendChild(row);}}
const process=item=>item.processRunning?'실행 중 / '+item.processId:'실행 안 됨';
const failure=item=>item.lifecycle?.lastFailure?time(item.lifecycle.failedAtUtc)+' · '+item.lifecycle.lastFailure:'—';
const modeLabels={running:'운영 중',maintenance:'점검 중',scheduled:'종료 예정'};
let modeTarget=null,modeBusy=false,resumeMode=false;
function syncModeTime(){document.getElementById('mode-time-row').hidden=resumeMode||document.getElementById('mode-kind').value!=='scheduled';}
function openMode(item,resume){
 if(modeBusy)return;modeTarget=item;resumeMode=resume;
 document.getElementById('mode-title').textContent=item.name+' — '+(resume?'운영 재개':'점검 설정');
 document.getElementById('mode-choice').hidden=resume;
 document.getElementById('mode-kind').value=item.operation.mode==='scheduled'?'scheduled':'maintenance';
 const at=item.operation.shutdownAtUtc?new Date(item.operation.shutdownAtUtc):null;
 document.getElementById('mode-time').value=at?new Date(at.getTime()-at.getTimezoneOffset()*60000).toISOString().slice(0,19):'';
 document.getElementById('mode-reason').value='';document.getElementById('mode-result').textContent='';
 document.getElementById('mode-apply').textContent=resume?'운영 재개 적용':'점검 설정 적용';
 document.getElementById('mode-form').hidden=false;syncModeTime();
}
document.getElementById('mode-kind').addEventListener('change',syncModeTime);
document.getElementById('mode-cancel').addEventListener('click',()=>{if(!modeBusy){modeTarget=null;document.getElementById('mode-form').hidden=true;}});
document.getElementById('mode-apply').addEventListener('click',async()=>{
 if(modeBusy||!modeTarget)return;
 const target=modeTarget,mode=resumeMode?'running':document.getElementById('mode-kind').value,reason=document.getElementById('mode-reason').value.trim();
 const value=document.getElementById('mode-time').value,date=value?new Date(value):null;
 if(!reason||(mode==='scheduled'&&(!date||isNaN(date.getTime())||date<=new Date()))){document.getElementById('mode-result').textContent='사유와 현재 이후의 종료 시각을 확인해주세요.';return;}
 modeBusy=true;for(const id of ['mode-kind','mode-time','mode-reason','mode-apply','mode-cancel'])document.getElementById(id).disabled=true;
 try{
  const response=await fetch('/api/operations/lobbies/'+encodeURIComponent(target.id)+'/mode',{method:'POST',headers:{'Content-Type':'application/json','X-Operations-Request':'1'},body:JSON.stringify({mode,reason,atUtc:mode==='scheduled'?date.toISOString():null,expectedRevision:target.operation.revision})});
  const result=await response.json();if(!response.ok){if(response.status===409){modeTarget=null;document.getElementById('mode-form').hidden=true;}throw new Error(result.message||'운영 설정 변경 실패');}
  document.getElementById('mode-result').textContent=result.message+(result.persistenceWarning?' '+result.persistenceWarning:'');modeTarget=null;document.getElementById('mode-form').hidden=true;
 }catch(error){document.getElementById('mode-result').textContent=error.message;}
 finally{modeBusy=false;for(const id of ['mode-kind','mode-time','mode-reason','mode-apply','mode-cancel'])document.getElementById(id).disabled=false;await refresh();}
});
let loading=false;

async function refresh(){if(loading)return;loading=true;document.getElementById('refresh').disabled=true;try{
const response=await fetch('/api/operations',{cache:'no-store'});if(!response.ok)throw new Error('운영 정보를 불러오지 못했습니다 (HTTP '+response.status+'). 서버 PC의 localhost에서 열어주세요.');const data=await response.json();
table('lobbies',data.lobbies,[x=>x.name+' / '+x.id,x=>modeLabels[x.operation.mode]+(x.operation.mode==='scheduled'?' · '+time(x.operation.shutdownAtUtc):'')+(x.operation.reason?' · '+x.operation.reason:''),x=>x.connectedUsers,x=>x.roomCount,x=>'']);

for(const [index,item]of data.lobbies.entries()){
 const cell=document.getElementById('lobbies').rows[index].lastElementChild,configure=document.createElement('button');configure.textContent=item.operation.mode==='running'?'점검 설정':'점검 설정 변경';configure.disabled=modeBusy;configure.addEventListener('click',()=>openMode(item,false));cell.appendChild(configure);
 if(item.operation.mode!=='running'){const resume=document.createElement('button');resume.textContent='운영 재개';resume.disabled=modeBusy;resume.addEventListener('click',()=>openMode(item,true));cell.appendChild(resume);}
}
table('rooms',data.rooms,[x=>x.name+' / '+x.id,x=>x.lobbyId,x=>state(x.state)+' / '+(x.trainingRunning?'훈련 진행 중':'훈련 대기'),process,x=>x.connectedUsers+' / '+x.maxUsers,x=>x.pendingUsers,x=>time(x.lifecycle?.startedAtUtc),x=>time(x.lifecycle?.stoppedAtUtc),failure]);
const eventLabels={shutdown_scheduled:'종료 예약',shutdown_cancelled:'종료 해제',shutdown_completed:'예약 종료 완료 → 점검 중'};
table('events',data.recentEvents,[x=>time(x.atUtc),x=>x.kind==='lobby'?'로비':'방',x=>x.lobbyId,x=>x.name+' / '+x.id,x=>eventLabels[x.action]||state(x.action),x=>x.reason||'—']);
document.getElementById('summary').textContent='로비 '+data.lobbies.length+'개 · 방 '+data.rooms.length+'개 · 실행 프로그램 '+data.rooms.filter(x=>x.processRunning).length+'개 · 서버 가동 '+Math.floor(data.uptimeSeconds/60)+'분';
document.getElementById('message').textContent='갱신 완료: '+time(data.generatedAtUtc);
document.getElementById('history').textContent=data.history.error?data.history.error:data.history.enabled?'운영 이력 파일 저장 사용 · 마지막 저장: '+time(data.history.savedAtUtc):'운영 이력 파일 저장 꺼짐 · 메모리에만 보관합니다.';
}catch(error){document.getElementById('message').textContent=error.message+' 이전 표시 정보는 최신 상태가 아닐 수 있습니다.';}finally{loading=false;document.getElementById('refresh').disabled=false;}}
document.getElementById('refresh').addEventListener('click',refresh);refresh();setInterval(refresh,5000);
</script></body></html>
""";
    }
}
