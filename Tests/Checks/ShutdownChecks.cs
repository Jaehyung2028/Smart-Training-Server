using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Operations;
using Smart_Training_Server.Services.Room;

internal static class ShutdownChecks
{
    private sealed class LobbyProcesses : ILobbyServerProcessManager
    {
        public readonly Dictionary<string,string> Secrets = new();
        public readonly HashSet<string> Running = new();
        public void Launch(string id,string exe,string secret){Secrets[id]=secret;Running.Add(id);}
        public void Stop(string id)=>Running.Remove(id);
        public bool IsRunning(string id)=>Running.Contains(id);
    }
    private sealed class RoomProcesses : IRoomServerProcessManager
    {
        public readonly HashSet<string> Running = new();
        public void Launch(string id,int port,string exe,string secret)=>Running.Add(id);
        public void Stop(string id)=>Running.Remove(id);
        public bool IsRunning(string id)=>Running.Contains(id);
    }
    public static async Task Run()
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: shutdown "+name);}
        var folder=Path.Combine(Path.GetTempPath(),"shutdown-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"history.json");var registry=new ServerOperationsRegistry(file);
            var users=new UserService(Options.Create(new AuthOptions{NameMinLength=1,NameMaxLength=30}));
            var lp=new LobbyProcesses();var rp=new RoomProcesses();
            var lobbies=new LobbyService(new ContentService(),users,Options.Create(new LobbyOptions()),lp,registry);lobbies.InitializeCatalogLobbies();
            var rooms=new RoomService(lobbies,users,Options.Create(new RoomOptions{NameMinLength=1,NameMaxLength=30,MinUsers=1,MaxUsers=4}),processManager:rp,operations:registry);
            var service=new LobbyShutdownService(users,lobbies,rooms,registry);
            var a=users.Login(new LoginRequest{UserName="shutdown-a"}).User!;var b=users.Login(new LoginRequest{UserName="shutdown-b"}).User!;var c=users.Login(new LoginRequest{UserName="shutdown-c"}).User!;
            Task<Smart_Training_Server.Responses.Lobby.JoinLobbyResponse> Join(string uid,string id)=>lobbies.JoinLobby(new JoinLobbyRequest{UserId=uid,LobbyId=id});
            Check(registry.GetShutdown("sample-training").State=="none","default is no schedule");
            Check(service.SetShutdown("missing",DateTimeOffset.UtcNow.AddMinutes(1),"x",0).StatusCode==404,"unknown lobby rejected");
            Check(service.SetShutdown("sample-training",DateTimeOffset.UtcNow.AddMinutes(-1),"x",0).StatusCode==400,"past time rejected");
            Check(service.SetShutdown("sample-training",DateTimeOffset.UtcNow.AddDays(31),"x",0).StatusCode==400,"over 30 days rejected");
            Check(service.SetShutdown("sample-training",DateTimeOffset.UtcNow.AddMinutes(1)," ",0).StatusCode==400,"blank reason rejected");
            var entry=Join(a.Id,"sample-training");lobbies.RegisterLobbyServer("sample-training",lp.Secrets["sample-training"],"lobby-a");Check((await entry).Success,"initial lobby entry");
            var other=Join(c.Id,"safety-training");lobbies.RegisterLobbyServer("safety-training",lp.Secrets["safety-training"],"lobby-c");await other;
            var room=rooms.CreateRoom(new CreateRoomRequest{UserId=a.Id,LobbyId="sample-training",ContentId="sample-training",RoomName="scheduled-room",MaxUsers=2}).Room!;
            var task=rooms.JoinRoom(new JoinRoomRequest{UserId=a.Id,LobbyId="sample-training",RoomId=room.Id});rooms.RegisterServer(room.Id,room.ServerToken,"room-a");var res=await task;rooms.ConfirmConnection(room.Id,"room-a",a.Id,res.AdmissionToken);
            var due=DateTimeOffset.UtcNow.AddMinutes(5);var set=service.SetShutdown("sample-training",due,"업데이트",0);
            Check(set.Success&&set.PersistenceWarning==null&&set.Shutdown?.Revision==1,"schedule saved immediately");
            Check(lobbies.GetLobbySummary("sample-training") is {ShutdownState:"scheduled",ShutdownReason:"업데이트"},"public notice published");
            Check(!(await Join(b.Id,"sample-training")).Success&&b.PendingLobbyId=="","new entry rejected without reservation");
            service.ExecuteDue(due.UtcDateTime.AddSeconds(-1));
            Check(a.CurrentRoomId==room.Id&&rp.Running.Contains(room.Id),"existing connection survives before deadline");
            Check(service.SetShutdown("sample-training",null,"취소",0).StatusCode==409,"stale cancellation rejected");
            Check(service.SetShutdown("sample-training",null,"취소",1).Success,"operator can cancel before deadline");
            service.ExecuteDue(due.UtcDateTime.AddSeconds(1));Check(a.CurrentRoomId==room.Id,"cancelled reservation never shuts down");
            var accepted=Join(b.Id,"sample-training");Check((await accepted).Success,"cancellation reopens admission");
            var pendingRoom=rooms.JoinRoom(new JoinRoomRequest{UserId=b.Id,LobbyId="sample-training",RoomId=room.Id});var pendingRes=await pendingRoom;
            Check(b.PendingRoomId==room.Id,"pending room reservation established");
            service.SetShutdown("sample-training",due,"재예약",2);
            service.ExecuteDue(due.UtcDateTime);
            Check(registry.GetShutdown("sample-training").State=="completed","deadline completes schedule");
            Check(a.CurrentLobbyId==""&&a.CurrentRoomId==""&&b.CurrentLobbyId==""&&b.PendingRoomId=="","all memberships and reservations cleared");
            Check(rooms.FindById(room.Id)==null&&!rp.Running.Contains(room.Id)&&!lp.Running.Contains("sample-training"),"all target room and lobby workers stopped");
            Check(c.CurrentLobbyId=="safety-training"&&lp.Running.Contains("safety-training"),"other lobby and member preserved");
            Check(!rooms.ConfirmConnection(room.Id,"room-a",b.Id,pendingRes.AdmissionToken).Success,"late room acknowledgement rejected");
            Check(users.FindById(a.Id)==a,"login survives evacuation");
            Check(!(await Join(a.Id,"sample-training")).Success,"completed lobby stays closed");
            lobbies.MaintainLobbyServers();Check(!lp.Running.Contains("sample-training"),"idle monitor cannot restart evacuated lobby");
            int events=registry.Snapshot().Events.Count;service.ExecuteDue(due.UtcDateTime.AddSeconds(5));Check(registry.Snapshot().Events.Count==events,"deadline tick is idempotent");
            Check(!registry.Snapshot().Events.Any(x=>x.Action=="failed"&&x.Reason=="로비 종료 예약 실행"),"planned closure is not a failure");
            var restored=new ServerOperationsRegistry(file);Check(restored.GetShutdown("sample-training") is {State:"completed",Reason:"재예약",Revision:4},"completed block persists after restart");
            Check(service.SetShutdown("sample-training",null,"재개",4).Success&&!registry.GetMaintenance("sample-training").Enabled,"one resume clears completed maintenance and shutdown");
            var reopened = Join(a.Id,"sample-training"); lobbies.RegisterLobbyServer("sample-training",lp.Secrets["sample-training"],"reopened"); Check((await reopened).Success,"one resume permits on-demand entry");
            var future=service.SetShutdown("safety-training",due,"미래 예약",0);var loaded=new ServerOperationsRegistry(file);Check(loaded.GetShutdown("safety-training").AtUtc==due.UtcDateTime,"future schedule persists with exact UTC time");
            loaded.CompleteSession();Check(loaded.SetShutdown("safety-training","safety",null,"x",1).StatusCode==503,"closing registry rejects changes");
            var d=users.Login(new LoginRequest{UserName="shutdown-pending"}).User!;
            var pendingLobby=Join(d.Id,"emergency-training");
            var pendingDue=DateTimeOffset.UtcNow.AddMinutes(1);service.SetShutdown("emergency-training",pendingDue,"준비 중 종료",0);
            Check(d.PendingLobbyId=="emergency-training","preexisting lobby waiter is preserved before deadline");
            service.ExecuteDue(pendingDue.UtcDateTime);
            Check(!(await pendingLobby).Success&&d.PendingLobbyId==""&&d.CurrentLobbyId=="","deadline invalidates pending lobby waiter");
            Check(!lp.Running.Contains("emergency-training"),"pending-only worker stops at deadline");
            Check(!lobbies.RegisterLobbyServer("emergency-training",lp.Secrets["emergency-training"],"late"),"late lobby registration cannot resurrect completed closure");
        }
        finally{Directory.Delete(folder,true);}
    }
}
