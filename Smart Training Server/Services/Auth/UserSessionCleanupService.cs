// HTTP 요청과 별개로 만료 사용자를 주기적으로 정리하는 백그라운드 작업입니다.
namespace Smart_Training_Server.Services.Auth
{
    public sealed class UserSessionCleanupService : BackgroundService
    {
        private readonly UserLifecycleService lifecycle;

        private readonly ILogger<UserSessionCleanupService> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public UserSessionCleanupService(UserLifecycleService lifecycle, ILogger<UserSessionCleanupService> logger)
        {
            this.lifecycle = lifecycle;
            this.logger = logger;
        }

        // 유예 기한을 넘긴 사용자를 주기적으로 정리합니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await lifecycle.CleanupExpiredUsersAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "만료 사용자 정리 중 오류가 발생했습니다.");
                    }
                }
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
