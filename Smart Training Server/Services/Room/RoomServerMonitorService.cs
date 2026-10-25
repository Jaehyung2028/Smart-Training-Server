// Unity 방 서버 프로세스의 생존 여부를 주기적으로 확인하고 종료된 서버의 상태 정리를 요청합니다.
namespace Smart_Training_Server.Services.Room
{
    public sealed class RoomServerMonitorService : BackgroundService
    {
        private readonly RoomService rooms;

        private readonly ILogger<RoomServerMonitorService> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public RoomServerMonitorService(RoomService rooms, ILogger<RoomServerMonitorService> logger)
        {
            this.rooms = rooms;
            this.logger = logger;
        }

        // Unity 방 프로세스 종료 여부를 주기적으로 확인합니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        rooms.CheckServerProcesses();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Unity 방 서버 감시 중 오류가 발생했습니다.");
                    }
                }
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
