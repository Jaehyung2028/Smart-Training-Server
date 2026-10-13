namespace Smart_Training_Server.Services.Room;

public sealed class RoomReservationCleanupService(RoomService rooms) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                rooms.ExpireReservations();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
