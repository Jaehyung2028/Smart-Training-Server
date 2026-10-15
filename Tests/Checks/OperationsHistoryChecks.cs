using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Options;
using Smart_Training_Server.Services.Operations;

internal static class OperationsHistoryChecks
{
    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        public readonly CancellationTokenSource Started = new(), Stopping = new(), Stopped = new();
        public CancellationToken ApplicationStarted => Started.Token;
        public CancellationToken ApplicationStopping => Stopping.Token;
        public CancellationToken ApplicationStopped => Stopped.Token;
        public void StopApplication() => Stopping.Cancel();
        public void Dispose() { Started.Dispose(); Stopping.Dispose(); Stopped.Dispose(); }
    }
    private sealed class Opener : IOperationsBrowserOpener
    {
        public readonly List<string> Urls = new();
        public bool Fail;
        public void Open(string url) { if (Fail) throw new IOException("fixture browser failure"); Urls.Add(url); }
    }
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: operations history " + name); }
        var folder = Path.Combine(Path.GetTempPath(), "operations-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "history.json");
            var registry = new ServerOperationsRegistry(file);
            registry.Record("room", "history-room", "sample-training", "saved room", "starting");
            registry.Record("room", "history-room", "sample-training", "saved room", "failed", "fixture recorded failure");
            registry.FlushHistory();
            Check(File.Exists(file) && registry.HistoryStatus() is { Enabled: true, SavedAtUtc: not null, Error: null }, "history saved with observable status");
            var restored = new ServerOperationsRegistry(file);
            Check(restored.Snapshot().Processes.Single() is { LastFailure: "fixture recorded failure", StartCount: 1 } && restored.Snapshot().Events.Count == 2, "failure lifecycle and events survive restart");
            var savedAt = registry.HistoryStatus().SavedAtUtc; registry.FlushHistory();
            Check(registry.HistoryStatus().SavedAtUtc == savedAt, "unchanged history does not rewrite the file");
            File.WriteAllText(file + ".tmp", "partial write");
            Check(new ServerOperationsRegistry(file).Snapshot().Processes.Count == 1, "partial temporary file does not replace valid history");
            registry.Record("lobby", "pending", "pending", "pending", "starting"); registry.FlushHistory();
            var interrupted = new ServerOperationsRegistry(file);
            Check(interrupted.Snapshot().Processes.Single(x => x.Id == "pending") is { State: "offline", FailedAtUtc: not null } && interrupted.Snapshot().Events.Any(x => x.Action == "interrupted"), "interrupted old process is historical offline data rather than current running state");
            interrupted.FlushHistory();
            Check(!new ServerOperationsRegistry(file).Snapshot().Processes.Any(x => x.State is "starting" or "running"), "interruption recovery is persisted without resurrecting old active processes");
            var normalFile = Path.Combine(folder, "normal.json");
            var normal = new ServerOperationsRegistry(normalFile);
            normal.Record("lobby", "normal", "normal", "normal", "starting"); normal.Record("lobby", "normal", "normal", "normal", "ready"); normal.CompleteSession();
            normal.Record("lobby", "normal", "normal", "normal", "failed", "late disconnect callback");
            var stopped = new ServerOperationsRegistry(normalFile);
            Check(stopped.Snapshot().Processes.Single() is { State: "offline", LastFailure: null, StoppedAtUtc: not null }, "normal shutdown is saved without false crash and ignores late callbacks");
            var corrupt = Path.Combine(folder, "corrupt.json"); File.WriteAllText(corrupt, "{broken");
            var repaired = new ServerOperationsRegistry(corrupt);
            Check(repaired.HistoryStatus().Error != null && Directory.GetFiles(folder, "corrupt.json.invalid-*").Length == 1, "corrupt history is preserved separately and surfaced without preventing startup");
            repaired.Record("room", "repaired", "fixture", "repaired", "failed", "new failure"); repaired.FlushHistory();
            Check(repaired.HistoryStatus().Error == null && new ServerOperationsRegistry(corrupt).Snapshot().Processes.Count == 1, "fresh history can be saved after corrupt-file recovery");
            var blocked = Path.Combine(folder, "blocked.json"); Directory.CreateDirectory(blocked);
            var retry = new ServerOperationsRegistry(blocked); retry.Record("room", "io", "fixture", "io", "failed", "retained"); retry.FlushHistory();
            Check(retry.HistoryStatus().Error != null && retry.Snapshot().Processes.Single().LastFailure == "retained", "write failure preserves memory data and reports failure");
            Directory.Delete(blocked); retry.FlushHistory();
            Check(retry.HistoryStatus().Error == null && File.Exists(blocked), "file save retries after an IO problem is resolved");
            var memory = new ServerOperationsRegistry(); memory.Record("room", "memory", "fixture", "memory", "starting"); memory.FlushHistory();
            Check(!memory.HistoryStatus().Enabled && memory.Snapshot().Processes.Count == 1, "disabled persistence keeps memory-only behavior");
        }
        finally
        {
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture cleanup path");
            Directory.Delete(folder, true);
        }
        var values = new Dictionary<string, string?> { ["Server:Port"] = "5999" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var lifetime = new Lifetime(); var opener = new Opener();
        var launcher = new OperationsBrowserLauncher(lifetime, config, Options.Create(new OperationsOptions()), opener, NullLogger<OperationsBrowserLauncher>.Instance);
        await launcher.StartAsync(default);
        Check(opener.Urls.Count == 0, "browser waits until application has started");
        lifetime.Started.Cancel(); await launcher.StartAsync(default);
        Check(opener.Urls.Count == 1 && opener.Urls[0] == "http://localhost:5999/operations", "browser opens operations once using actual configured API port");
        await launcher.StopAsync(default);
        using var disabledLife = new Lifetime(); var disabledOpener = new Opener();
        var disabled = new OperationsBrowserLauncher(disabledLife, config, Options.Create(new OperationsOptions { OpenBrowserOnStart = false }), disabledOpener, NullLogger<OperationsBrowserLauncher>.Instance);
        await disabled.StartAsync(default); disabledLife.Started.Cancel();
        Check(disabledOpener.Urls.Count == 0, "automatic browser launch can be disabled");
        using var failedLife = new Lifetime(); var failedOpener = new Opener { Fail = true };
        var failed = new OperationsBrowserLauncher(failedLife, config, Options.Create(new OperationsOptions()), failedOpener, NullLogger<OperationsBrowserLauncher>.Instance);
        await failed.StartAsync(default); failedLife.Started.Cancel();
        Check(failedOpener.Urls.Count == 0, "browser failure does not fail server startup");
        using var stoppedLife = new Lifetime(); var stoppedOpener = new Opener();
        var stoppedLauncher = new OperationsBrowserLauncher(stoppedLife, config, Options.Create(new OperationsOptions()), stoppedOpener, NullLogger<OperationsBrowserLauncher>.Instance);
        await stoppedLauncher.StartAsync(default); await stoppedLauncher.StopAsync(default); stoppedLife.Started.Cancel();
        Check(stoppedOpener.Urls.Count == 0, "stopping before application started prevents browser launch");
        using var probeLife = new Lifetime(); var probeOpener = new Opener();
        values["SMART_TRAINING_SERVER_PROBE"] = "1";
        var probeConfig = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var probe = new OperationsBrowserLauncher(probeLife, probeConfig, Options.Create(new OperationsOptions()), probeOpener, NullLogger<OperationsBrowserLauncher>.Instance);
        await probe.StartAsync(default); probeLife.Started.Cancel();
        Check(probeOpener.Urls.Count == 0, "independent client test mode does not open browser tabs");
    }
}
