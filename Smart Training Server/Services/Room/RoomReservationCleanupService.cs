// 기한이 지난 입장 예약을 주기적으로 해제합니다. 클라이언트가 접속하지 않아도 정원이 영구 점유되지 않게 합니다.
namespace Smart_Training_Server.Services.Room
{
    public sealed class RoomReservationCleanupService : BackgroundService
    {
        private readonly RoomService rooms;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public RoomReservationCleanupService(RoomService rooms)
        {
            this.rooms = rooms;
        }

        // 만료된 방 입장 예약을 주기적으로 해제합니다. stoppingToken은 API 종료 시 반복 대기를 끝내는 취소 신호입니다.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    rooms.ExpireReservations();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
