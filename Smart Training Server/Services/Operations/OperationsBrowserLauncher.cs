using System.Diagnostics;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;

namespace Smart_Training_Server.Services.Operations;

public interface IOperationsBrowserOpener { void Open(string url); }
public sealed class OperationsBrowserOpener : IOperationsBrowserOpener
{
    public void Open(string url)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

public sealed class OperationsBrowserLauncher(IHostApplicationLifetime lifetime, IConfiguration configuration,
    IOptions<OperationsOptions> options, IOperationsBrowserOpener opener, ILogger<OperationsBrowserLauncher> logger) : IHostedService
{
    private IDisposable? registration;
    private int opened;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.OpenBrowserOnStart && configuration["SMART_TRAINING_SERVER_PROBE"] != "1")
            registration = lifetime.ApplicationStarted.Register(OpenOnce);
        return Task.CompletedTask;
    }
    private void OpenOnce()
    {
        if (Interlocked.Exchange(ref opened, 1) != 0) return;
        string url = "http://localhost:" + configuration.GetValue<int>("Server:Port", 5229) + "/operations";
        try { opener.Open(url); logger.LogInformation("운영 화면 주소: {Url}", url); }
        catch (Exception error) { logger.LogWarning(error, "운영 화면 자동 열기에 실패했습니다. {Url}에서 직접 열어주세요.", url); }
    }
    public Task StopAsync(CancellationToken cancellationToken) { registration?.Dispose(); return Task.CompletedTask; }
}
