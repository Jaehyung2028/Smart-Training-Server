namespace Smart_Training_Server.Services.Room;

public sealed class RoomServerMonitorService(RoomService rooms, ILogger<RoomServerMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { rooms.CheckServerProcesses(); }
                catch (Exception ex) { logger.LogError(ex, "Unity 방 서버 감시 중 오류가 발생했습니다."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
