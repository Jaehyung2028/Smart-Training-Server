using Smart_Training_Server.Helper;
namespace Smart_Training_Server.Services.Lobby;
public interface ILobbyServerProcessManager
{
    void Launch(string id, string executable, string secret);
    bool IsRunning(string id);
    void Stop(string id);
}
public sealed class LobbyServerProcessManager(string apiUrl = "http://127.0.0.1:5229") : ILobbyServerProcessManager
{
    public void Launch(string id, string executable, string secret) => ProcessHelper.LaunchUnityServer(id, 0, executable, secret, apiUrl, true);
    public bool IsRunning(string id) => ProcessHelper.IsProcessRunning("lobby:" + id);
    public void Stop(string id) => ProcessHelper.KillProcessById("lobby:" + id);
}
public sealed class LobbyServerMonitorService(LobbyService lobbies) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) lobbies.MaintainLobbyServers();
    }
}
