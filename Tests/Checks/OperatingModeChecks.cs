using System.Text.Json;
using Smart_Training_Server.Services.Operations;

internal static class OperatingModeChecks
{
    public static Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: operating mode " + name);
        }

        var folder = Path.Combine(Path.GetTempPath(), "unified-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "history.json");
            var r = new ServerOperationsRegistry(file);
            var due = DateTime.UtcNow.AddMinutes(5);
            Check(
                r.SetOperatingMode("lobby", "name", "maintenance", "차단", null, 0).Success,
                "enter admission-only maintenance"
            );
            Check(
                r.SetOperatingMode("lobby", "name", "scheduled", "예약", due, 1).Success && r.GetMaintenance("lobby").Enabled,
                "switch maintenance to scheduled with one shared revision"
            );
            Check(
                r.SetOperatingMode("lobby", "name", "running", "오래된 재개", null, 1).StatusCode == 409,
                "old maintenance request cannot overwrite newer scheduled mode"
            );
            Check(
                r.SetOperatingMode("lobby", "name", "maintenance", "차단 유지", null, 2).Success && r.GetShutdown("lobby").AtUtc == null,
                "switch to admission-only removes deadline"
            );
            r.CompleteShutdown("lobby", "name");
            Check(r.GetOperatingMode("lobby").Revision == 3, "cancelled deadline cannot later complete");
            r.SetOperatingMode("lobby", "name", "scheduled", "종료", due, 3);
            r.CompleteShutdown("lobby", "name");
            Check(
                r.GetOperatingMode("lobby")is { Mode: "maintenance", Revision: 5 } && r.GetMaintenance("lobby").Enabled,
                "deadline transitions to maintenance"
            );
            Check(
                r.SetOperatingMode("lobby", "name", "running", "운영 재개", null, 5).Success && r.GetOperatingMode("lobby").ShutdownAtUtc == null && !r.GetMaintenance("lobby").Enabled,
                "one resume clears deadline and admission block"
            );
            r.FlushHistory();
            Check(
                new ServerOperationsRegistry(file).GetOperatingMode("lobby")is { Mode: "running", Revision: 6 },
                "unified mode and revision persist"
            );
            int events = r.Snapshot().Events.Count;
            r.SetOperatingMode("lobby", "name", "running", "운영 재개", null, 5);
            Check(r.Snapshot().Events.Count == events, "identical repeated command is idempotent");
            Check(
                r.SetOperatingMode("lobby", "name", "unknown", "bad", null, 6).StatusCode == 400 && r.SetOperatingMode("lobby", "name", "maintenance", "bad", due, 6).StatusCode == 400,
                "invalid mode and mismatched deadline rejected"
            );
            void Legacy(string state, bool maintenance)
            {
                File.WriteAllText(
                    file,
                    JsonSerializer.Serialize(new { Version = 1, Processes = Array.Empty<ProcessLifecycle>(), Events = Array.Empty<ServerOperationEvent>(), Maintenance = new[] { new LobbyMaintenance("old", maintenance, "기존 점검", DateTime.UtcNow, 2) }, Shutdowns = new[] { new LobbyShutdown("old", state == "none" ? null : (DateTime? )due, "기존 종료", state, 3, DateTime.UtcNow) } })
                );
            }

            Legacy("scheduled", true);
            var migrated = new ServerOperationsRegistry(file);
            Check(
                migrated.GetOperatingMode("old")is { Mode: "scheduled", Reason: "기존 종료", Revision: 5 },
                "legacy overlap preserves scheduled deadline and combines versions"
            );
            migrated.SetOperatingMode("old", "old", "running", "통합 재개", null, 5);
            Check(
                !migrated.GetMaintenance("old").Enabled && migrated.GetShutdown("old").State == "none",
                "migrated overlapping settings release in one command"
            );
            Legacy("completed", false);
            Check(
                new ServerOperationsRegistry(file).GetOperatingMode("old").Mode == "maintenance",
                "legacy completed shutdown migrates to maintenance"
            );
            Legacy("none", true);
            Check(
                new ServerOperationsRegistry(file).GetOperatingMode("old").Mode == "maintenance",
                "legacy admission block migrates to maintenance"
            );
            Legacy("none", false);
            Check(
                new ServerOperationsRegistry(file).GetOperatingMode("old").Mode == "running",
                "legacy open lobby stays running"
            );
            migrated.FlushHistory();
            using var saved = JsonDocument.Parse(File.ReadAllText(file));
            Check(
                saved.RootElement.GetProperty("Modes").GetArrayLength() == 1 && saved.RootElement.GetProperty("Maintenance").ValueKind == JsonValueKind.Null,
                "new persistence contains one authoritative mode collection"
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        return Task.CompletedTask;
    }
}
