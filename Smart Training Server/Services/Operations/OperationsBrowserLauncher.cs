using Smart_Training_Server.Options;
using System.Diagnostics;
using Microsoft.Extensions.Options;

// API 준비 완료 후 서버 PC의 운영 페이지를 한 번 엽니다. 브라우저 열기와 API 실행 성공은 별개로 처리합니다.
namespace Smart_Training_Server.Services.Operations
{
    public interface IOperationsBrowserOpener
    {
        void Open(string url);
    }

    public sealed class OperationsBrowserOpener : IOperationsBrowserOpener
    {
        // 대화형 Windows 환경이면 지정 URL을 기본 브라우저로 엽니다.
        public void Open(string url)
        {
            if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            {
                return;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    public sealed class OperationsBrowserLauncher : IHostedService
    {
        private readonly IHostApplicationLifetime lifetime;

        private readonly IConfiguration configuration;

        private readonly IOptions<OperationsOptions> options;

        private readonly IOperationsBrowserOpener opener;

        private readonly ILogger<OperationsBrowserLauncher> logger;

        // 생성에 필요한 값을 전달받아 초기화합니다.
        public OperationsBrowserLauncher(
            IHostApplicationLifetime lifetime,
            IConfiguration configuration,
            IOptions<OperationsOptions> options,
            IOperationsBrowserOpener opener,
            ILogger<OperationsBrowserLauncher> logger
        )
        {
            this.lifetime = lifetime;
            this.configuration = configuration;
            this.options = options;
            this.opener = opener;
            this.logger = logger;
        }

        private IDisposable? registration;

        private int opened;

        // 호스트가 서비스를 시작할 때 호출합니다. 실제 API 준비 완료 시점에 운영 페이지를 열도록 연결합니다.
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (options.Value.OpenBrowserOnStart && configuration["SMART_TRAINING_SERVER_PROBE"] != "1")
            {
                registration = lifetime.ApplicationStarted.Register(OpenOnce);
            }

            return Task.CompletedTask;
        }

        // 설정과 중복 실행 여부를 확인해 운영 페이지를 한 번만 엽니다. 열기 실패는 로그로 남깁니다.
        private void OpenOnce()
        {
            if (Interlocked.Exchange(ref opened, 1) != 0)
            {
                return;
            }

            string url = "http://localhost:" + configuration.GetValue<int>("Server:Port", 5229) + "/operations";
            try
            {
                opener.Open(url);
                logger.LogInformation("운영 화면 주소: {Url}", url);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "운영 화면 자동 열기에 실패했습니다. {Url}에서 직접 열어주세요.", url);
            }
        }

        // 호스트 종료 시 작업과 등록된 자원을 정리합니다. 이력 작성 서비스는 마지막 운영 기록도 저장합니다.
        public Task StopAsync(CancellationToken cancellationToken)
        {
            registration?.Dispose();
            return Task.CompletedTask;
        }
    }
}
