using System.Security.Cryptography;
using System.Text;
using Smart_Training_Server.Models;

// 인증 토큰의 생성·검사·갱신·폐기를 담당하는 UserService의 일부입니다. 저장소에는 토큰 해시를 보관합니다.
namespace Smart_Training_Server.Services.Auth
{
    public partial class UserService
    {
        // 클라이언트의 토큰 원문은 응답으로만 전달하고 서버에는 해시를 보관합니다.
        private sealed record UserSession(string UserId, DateTime ExpiresAtUtc);

        // 토큰 해시 → 사용자 ID·만료 시각 저장소입니다. 공유 잠금으로 접근을 보호합니다.
        private readonly Dictionary<string, UserSession> sessions = new();
        private readonly TimeProvider timeProvider;

        // 토큰 원문을 SHA-256으로 바꿔 조회 키로 사용합니다. 저장소 유출 시 원문 노출을 줄이기 위한 처리입니다.
        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        // 암호학적 난수 32바이트를 64자리 문자열로 만듭니다. 원문은 반환하고 저장소에는 해시를 넣습니다.
        private (string Token, DateTime ExpiresAtUtc) CreateSession(string userId)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(authOptions.AccessTokenLifetimeMinutes);
            sessions.Add(HashToken(token), new UserSession(userId, expires));
            return (token, expires);
        }

        // 인증 요청마다 토큰 형식·존재·기한을 확인합니다. 토큰 만료와 사용자 정리는 별개 절차입니다.
        public UserInfo? FindByAccessToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length != 64)
            {
                return null;
            }

            lock (MembershipLock)
            {
                if (!sessions.TryGetValue(HashToken(token), out var session))
                {
                    return null;
                }

                if (session.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    return null;
                }

                return FindById(session.UserId);
            }
        }

        // 유예 기한 안에서는 만료 토큰도 갱신할 수 있습니다. 새 토큰을 발급하면 기존 토큰은 폐기합니다.
        public Smart_Training_Server.Responses.Auth.LoginResponse RefreshSession(string token)
        {
            lock (MembershipLock)
            {
                if (string.IsNullOrEmpty(token) ||
                    token.Length != 64 ||
                    !sessions.TryGetValue(HashToken(token), out var old) ||
                    old.ExpiresAtUtc.AddMinutes(authOptions.SessionGraceMinutes) <= timeProvider.GetUtcNow().UtcDateTime ||
                    FindById(old.UserId) is not { } user)
                {
                    return new()
                    {
                        Success = false,
                        Message = "갱신할 수 없는 인증 토큰입니다."
                    };
                }

                var next = CreateSession(user.Id);
                sessions.Remove(HashToken(token));
                return new()
                {
                    Success = true,
                    Message = "인증 토큰이 갱신되었습니다.",
                    User = user,
                    AccessToken = next.Token,
                    ExpiresAtUtc = next.ExpiresAtUtc
                };
            }
        }

        // 갱신 유예 기한까지 지난 사용자를 고릅니다. 정리 작업이 이 목록을 사용합니다.
        internal IReadOnlyList<string> GetUsersPastSessionGrace()
        {
            lock (MembershipLock)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var retained = sessions.Values.Where(x => x.ExpiresAtUtc.AddMinutes(authOptions.SessionGraceMinutes) > now).Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);
                return users.Keys.Where(id => !retained.Contains(id)).ToArray();
            }
        }

        // 해당 사용자의 모든 세션을 무효화하여 기존 토큰을 다시 사용할 수 없게 합니다.
        internal void RevokeSessions(string userId)
        {
            lock (MembershipLock)
            {
                foreach (var pair in sessions.Where(x => x.Value.UserId == userId).ToArray())
                {
                    sessions.Remove(pair.Key);
                }
            }
        }

        // 인증 세션을 먼저 폐기하고 사용자 저장소에서 제거합니다. 소속 방·로비 정리는 상위 서비스의 책임입니다.
        internal bool RemoveUser(string userId)
        {
            lock (MembershipLock)
            {
                RevokeSessions(userId);
                return users.TryRemove(userId, out _);
            }
        }
    }
}
