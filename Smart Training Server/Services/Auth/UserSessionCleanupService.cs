namespace Smart_Training_Server.Services.Auth;

public sealed class UserSessionCleanupService(UserLifecycleService lifecycle, ILogger<UserSessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await lifecycle.CleanupExpiredUsersAsync(); }
                catch (Exception ex) { logger.LogError(ex, "만료 사용자 정리 중 오류가 발생했습니다."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
