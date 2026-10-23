using Microsoft.AspNetCore.SignalR;
using Smart_Training_Server.Services.Lobby;

// 실제 그룹 API 호출을 기록하여 로비 간 격리와 만료 연결 제거를 확인합니다.
internal sealed class TestSignalRGroups : IGroupManager
{
    private readonly Dictionary<string, HashSet<string>> members = new();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (!members.TryGetValue(groupName, out var group))
        {
            members[groupName] = group = new();
        }

        group.Add(connectionId);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (members.TryGetValue(groupName, out var group))
        {
            group.Remove(connectionId);
        }

        return Task.CompletedTask;
    }

    public string[] Members(string? groupName)
    {
        return groupName != null && members.TryGetValue(groupName, out var group) ? group.Order().ToArray() : [];
    }

    public async Task Synchronize(LobbyRoomUpdates updates)
    {
        // 내부의 실제 그룹 갱신 함수를 호출해 가짜 권한 판별을 만들지 않습니다.
        var method = typeof(LobbyRoomUpdates).GetMethod(
            "SynchronizeGroupsAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(updates, new object?[] { this, null })!;
        var roomMethod = typeof(LobbyRoomUpdates).GetMethod(
            "SynchronizeRoomGroupsAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)roomMethod!.Invoke(updates, new object?[] { this, null })!;
    }
}
