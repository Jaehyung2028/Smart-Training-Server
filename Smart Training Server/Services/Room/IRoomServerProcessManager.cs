using Smart_Training_Server.Helper;

namespace Smart_Training_Server.Services.Room;

public interface IRoomServerProcessManager
{
    void Launch(string roomId, int port, string executablePath, string serverToken);
    bool IsRunning(string roomId);
    void Stop(string roomId);
}

public sealed class RoomServerProcessManager : IRoomServerProcessManager
{
    private readonly string apiBaseUrl;
    public RoomServerProcessManager(string apiBaseUrl = "http://127.0.0.1:5229") => this.apiBaseUrl = apiBaseUrl;
    public void Launch(string roomId, int port, string executablePath, string serverToken) =>
        ProcessHelper.LaunchUnityServer(roomId, port, executablePath, serverToken, apiBaseUrl);
    public bool IsRunning(string roomId) => ProcessHelper.IsProcessRunning(roomId);
    public void Stop(string roomId) => ProcessHelper.KillProcessById(roomId);
}
