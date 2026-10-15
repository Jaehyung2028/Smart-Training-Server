using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Smart_Training_Server._Controllers;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Operations;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Operations;
using Smart_Training_Server.Services.Room;

internal static class MaintenanceChecks
{
    private sealed class LobbyProcesses : ILobbyServerProcessManager
    {
        public readonly Dictionary<string,string> Secrets = new();
        public readonly HashSet<string> Running = new();
        public int Launches;
        public void Launch(string id,string executable,string secret){Launches++;Secrets[id]=secret;Running.Add(id);}
        public bool IsRunning(string id)=>Running.Contains(id);
        public void Stop(string id)=>Running.Remove(id);
    }
    private sealed class RoomProcesses : IRoomServerProcessManager
    {
        public bool Running;
        public void Launch(string id,int port,string executable,string secret)=>Running=true;
        public bool IsRunning(string id)=>Running;
        public void Stop(string id)=>Running=false;
    }
    public static async Task Run()
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: maintenance "+name);}
        var folder=Path.Combine(Path.GetTempPath(),"maintenance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"history.json");var registry=new ServerOperationsRegistry(file);
            var users=new UserService(Options.Create(new AuthOptions{NameMinLength=1,NameMaxLength=20}));
            var processes=new LobbyProcesses();var lobbies=new LobbyService(new ContentService(),users,Options.Create(new LobbyOptions()),processes,registry);lobbies.InitializeCatalogLobbies();
            var roomProcesses=new RoomProcesses();var rooms=new RoomService(lobbies,users,Options.Create(new RoomOptions{NameMinLength=1,NameMaxLength=20,MinUsers=1,MaxUsers=4}),processManager:roomProcesses,operations:registry);
            var ops=new ServerOperationsService(users,lobbies,rooms,registry);
            var a=users.Login(new LoginRequest{UserName="maintenance-a"}).User!;var b=users.Login(new LoginRequest{UserName="maintenance-b"}).User!;
            Task<Smart_Training_Server.Responses.Lobby.JoinLobbyResponse> Join(string id)=>lobbies.JoinLobby(new JoinLobbyRequest{UserId=id,LobbyId="sample-training"});
            Check(lobbies.GetLobbySummary("sample-training") is {IsMaintenance:false,MaintenanceRevision:0},"catalog starts in normal mode");
            var on=lobbies.SetMaintenance("sample-training",true,"정기 점검",0);
            Check(on.Success&&on.PersistenceWarning==null&&on.Maintenance is {Enabled:true,Revision:1,ChangedAtUtc:not null}&&File.Exists(file),"enable is saved with timestamp and revision");
            Check(processes.Launches==0&&registry.Snapshot().Processes.Count==0,"maintenance does not launch or alter a process lifecycle");
            var denied=await Join(a.Id);
            Check(!denied.Success&&denied.Message.Contains("정기 점검")&&a.PendingLobbyId==""&&processes.Launches==0,"new admission is rejected before reservation or process launch");
            Check(lobbies.GetLobbySummary("sample-training") is {IsMaintenance:true,MaintenanceReason:"정기 점검",PendingUsers:0},"public summary contains maintenance notice");
            int eventCount=registry.Snapshot().Events.Count;
            Check(lobbies.SetMaintenance("sample-training",true,"정기 점검",0).Success&&registry.Snapshot().Events.Count==eventCount,"duplicate request is idempotent without extra event");
            Check(lobbies.SetMaintenance("sample-training",false,"해제",0).StatusCode==409&&registry.GetMaintenance("sample-training").Enabled,"stale revision cannot overwrite current maintenance");
            Check(lobbies.SetMaintenance("sample-training",true," ",1).StatusCode==400&&lobbies.SetMaintenance("sample-training",true,new string('x',201),1).StatusCode==400,"blank and oversized reasons are rejected");
            Check(lobbies.SetMaintenance("missing",true,"점검",0).StatusCode==404,"unknown catalog lobby cannot be changed");
            Check(lobbies.SetMaintenance("sample-training",false,"점검 완료",1).Success,"maintenance can be released");
            var pending=Join(a.Id);
            Check(lobbies.GetLobbySummary("sample-training")!.PendingUsers==1,"admission accepted before switch waits normally");
            Check(lobbies.SetMaintenance("sample-training",true,"추가 점검",2).Success&&processes.Running.Contains("sample-training"),"switch does not stop worker serving existing pending demand");
            lobbies.RegisterLobbyServer("sample-training",processes.Secrets["sample-training"],"maintenance-lobby");
            Check((await pending).Success&&a.CurrentLobbyId=="sample-training", "request accepted before switch completes during maintenance");
            Check(!(await Join(b.Id)).Success&&processes.Launches==1,"requests after switch are blocked without duplicate launch");
            var room=rooms.CreateRoom(new CreateRoomRequest{UserId=a.Id,LobbyId="sample-training",ContentId="sample-training",RoomName="maintenance-room",MaxUsers=2}).Room!;
            var admission=rooms.JoinRoom(new JoinRoomRequest{UserId=a.Id,LobbyId="sample-training",RoomId=room.Id});rooms.RegisterServer(room.Id,room.ServerToken,"maintenance-room-server");var reservation=await admission;
            Check(rooms.ConfirmConnection(room.Id,"maintenance-room-server",a.Id,reservation.AdmissionToken).Success&&room.Users.Count==1,"existing lobby participant can create and join a room during maintenance");
            var live=ops.GetSnapshot();
            Check(live.Lobbies.Single(x=>x.Id=="sample-training").Maintenance?.Enabled==true&&live.Rooms.Single().ConnectedUsers==1&&live.Rooms.Single().Lifecycle?.State=="running","maintenance does not overwrite active room readiness");
            Check(lobbies.SetMaintenance("sample-training",true,"사유 변경",3).Success&&room.Users.Count==1&&roomProcesses.Running,"reason update preserves existing multiplayer connection");
            Check(lobbies.GetLobbySummary("safety-training") is {IsMaintenance:false,MaintenanceRevision:0},"another lobby is unaffected");
            OperationsController Controller(string peer,string? origin,bool custom){var context=new DefaultHttpContext();context.Connection.RemoteIpAddress=IPAddress.Parse(peer);context.Request.Scheme="http";context.Request.Host=new HostString("localhost:5229");context.Request.ContentType="application/json";if(origin!=null)context.Request.Headers.Origin=origin;if(custom)context.Request.Headers["X-Operations-Request"]="1";return new OperationsController(ops){ControllerContext=new ControllerContext{HttpContext=context}};}
            var request=new SetMaintenanceRequest{Enabled=true,Reason="운영 점검",ExpectedRevision=0};
            Check(Controller("203.0.113.7",null,true).SetMaintenance("safety-training",request) is ObjectResult{StatusCode:403},"remote mutation is blocked even with custom header");
            Check(Controller("127.0.0.1",null,false).SetMaintenance("safety-training",request) is ObjectResult{StatusCode:403},"missing mutation header is rejected");
            Check(Controller("127.0.0.1","https://attacker.example",true).SetMaintenance("safety-training",request) is ObjectResult{StatusCode:403},"cross-origin mutation is rejected");
            Check(Controller("127.0.0.1","http://localhost:5229",true).SetMaintenance("safety-training",request) is OkObjectResult,"local same-origin mutation with custom header is accepted");
            rooms.LeaveRoom(new LeaveRoomRequest{UserId=a.Id,LobbyId="sample-training",RoomId=room.Id});lobbies.LeaveLobby(new LeaveLobbyRequest{UserId=a.Id,LobbyId="sample-training"});registry.FlushHistory();
            Check(!processes.Running.Contains("sample-training")&&registry.GetMaintenance("sample-training").Enabled,"last departure still stops worker without clearing maintenance");
            var restoredRegistry=new ServerOperationsRegistry(file);var restoredProcesses=new LobbyProcesses();var restored=new LobbyService(new ContentService(),users,Options.Create(new LobbyOptions()),restoredProcesses,restoredRegistry);restored.InitializeCatalogLobbies();
            Check(restored.GetLobbySummary("sample-training") is {IsMaintenance:true,MaintenanceReason:"사유 변경",MaintenanceRevision:4}&&restoredProcesses.Launches==0,"restart restores maintenance without launching prior processes");
            Check(!(await restored.JoinLobby(new JoinLobbyRequest{UserId=b.Id,LobbyId="sample-training"})).Success,"restored maintenance blocks new admission");
            Check(restored.SetMaintenance("sample-training",false,"운영 재개",4).Success,"restored maintenance can be released using current revision");
            var next=restored.JoinLobby(new JoinLobbyRequest{UserId=b.Id,LobbyId="sample-training"});restored.RegisterLobbyServer("sample-training",restoredProcesses.Secrets["sample-training"],"restored");
            Check((await next).Success,"new admission succeeds after maintenance release");
            Check(restoredRegistry.Snapshot().Events.Any(x=>x.Action=="maintenance_on")&&restoredRegistry.Snapshot().Events.Any(x=>x.Action=="maintenance_off"),"enable and release events retain reason and time across restart");
            restoredRegistry.CompleteSession();Check(restored.SetMaintenance("sample-training",true,"종료 중",5).StatusCode==503,"shutdown prevents further maintenance changes");
            var old=Path.Combine(folder,"old.json");File.WriteAllText(old,"{\"Version\":1,\"Processes\":[],\"Events\":[]}");Check(!new ServerOperationsRegistry(old).GetMaintenance("sample-training").Enabled,"previous history format without maintenance remains compatible");
        }
        finally{if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe fixture path");Directory.Delete(folder,true);}
    }
}
