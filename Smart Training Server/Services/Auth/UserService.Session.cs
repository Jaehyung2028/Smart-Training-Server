using System.Security.Cryptography;
using System.Text;
using Smart_Training_Server.Models;

namespace Smart_Training_Server.Services.Auth;

public partial class UserService
{
    // 클라이언트의 토큰 원문은 응답으로만 전달하고 서버에는 해시를 보관합니다.
    private sealed record UserSession(string UserId, DateTime ExpiresAtUtc);
    private readonly Dictionary<string, UserSession> sessions = new();
    private readonly TimeProvider timeProvider;
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private (string Token, DateTime ExpiresAtUtc) CreateSession(string userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(authOptions.AccessTokenLifetimeMinutes);
        sessions.Add(HashToken(token), new UserSession(userId, expires));
        return (token, expires);
    }

    public UserInfo? FindByAccessToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64) return null;
        lock (MembershipLock)
        {
            if (!sessions.TryGetValue(HashToken(token), out var session)) return null;
            if (session.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
            {
                return null;
            }
            return FindById(session.UserId);
        }
    }

    public Smart_Training_Server.Responses.Auth.LoginResponse RefreshSession(string token)
    {
        lock (MembershipLock)
        {
            if (string.IsNullOrEmpty(token) || token.Length != 64 ||
                !sessions.TryGetValue(HashToken(token), out var old) ||
                old.ExpiresAtUtc.AddMinutes(authOptions.SessionGraceMinutes) <= timeProvider.GetUtcNow().UtcDateTime ||
                FindById(old.UserId) is not { } user)
                return new() { Success = false, Message = "갱신할 수 없는 인증 토큰입니다." };
            var next = CreateSession(user.Id);
            sessions.Remove(HashToken(token));
            return new()
            {
                Success = true, Message = "인증 토큰이 갱신되었습니다.", User = user,
                AccessToken = next.Token, ExpiresAtUtc = next.ExpiresAtUtc
            };
        }
    }

    internal IReadOnlyList<string> GetUsersPastSessionGrace()
    {
        lock (MembershipLock)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var retained = sessions.Values.Where(x => x.ExpiresAtUtc.AddMinutes(authOptions.SessionGraceMinutes) > now)
                .Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);
            return users.Keys.Where(id => !retained.Contains(id)).ToArray();
        }
    }

    internal void RevokeSessions(string userId)
    {
        lock (MembershipLock)
        {
            foreach (var pair in sessions.Where(x => x.Value.UserId == userId).ToArray())
                sessions.Remove(pair.Key);
        }
    }

    internal bool RemoveUser(string userId)
    {
        lock (MembershipLock)
        {
            RevokeSessions(userId);
            return users.TryRemove(userId, out _);
        }
    }
}
