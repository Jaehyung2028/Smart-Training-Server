namespace Smart_Training_Server.Services.Operations;

public sealed record ProcessLifecycle(string Key, string Kind, string Id, string LobbyId, string Name,
    string State, DateTime? StartedAtUtc, DateTime? StoppedAtUtc, DateTime? FailedAtUtc,
    string? LastFailure, int StartCount, DateTime UpdatedAtUtc);
public sealed record ServerOperationEvent(DateTime AtUtc, string Kind, string Id, string LobbyId,
    string Name, string Action, string? Reason);

// Operational metadata only; it intentionally contains no user or authentication data.
public sealed partial class ServerOperationsRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, ProcessLifecycle> processes = new();
    private readonly Queue<ServerOperationEvent> events = new();
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    public void Record(string kind, string id, string lobbyId, string name, string action, string? reason = null)
    {
        lock (gate)
        {
            if (closing) return;
            string key = kind + ":" + id;
            processes.TryGetValue(key, out var previous);
            // Cleanup can be called repeatedly; do not turn it into duplicate stop events.
            if (action == "stopped" && (previous == null || previous.State is "offline" or "failed")) return;
            if (action == "ready" && previous?.State == "running") return;
            var now = DateTime.UtcNow;
            processes[key] = new(key, kind, id, lobbyId, name,
                action switch { "starting" => "starting", "ready" => "running", "failed" => "failed", _ => "offline" },
                action == "starting" ? now : previous?.StartedAtUtc,
                action is "stopped" or "failed" ? now : previous?.StoppedAtUtc,
                action == "failed" ? now : previous?.FailedAtUtc,
                action == "failed" ? reason : previous?.LastFailure,
                (previous?.StartCount ?? 0) + (action == "starting" ? 1 : 0), now);
            events.Enqueue(new(now, kind, id, lobbyId, name, action, reason));
            revision++;
            while (events.Count > 200) events.Dequeue();
            foreach (var old in processes.Values.Where(x => x.State is "offline" or "failed")
                .OrderByDescending(x => x.UpdatedAtUtc).Skip(512).ToArray()) processes.Remove(old.Key);
            Console.WriteLine($"[Operations] kind={kind} lobbyId={lobbyId} id={id} event={action} at={now:O} reason={reason}");
        }
    }
    public (IReadOnlyList<ProcessLifecycle> Processes, IReadOnlyList<ServerOperationEvent> Events) Snapshot()
    {
        lock (gate) return (processes.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(), events.Reverse().ToArray());
    }
}
