using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Responses.Auth;
using System.Collections.Concurrent;

namespace Smart_Training_Server.Services.Auth
{
    public partial class UserService
    {
        // 로비 이동과 방 예약/확정의 사용자 상태 변경을 함께 보호합니다.
        internal object MembershipLock { get; } = new();

        private readonly ConcurrentDictionary<string, UserInfo> users = new();

        private readonly AuthOptions authOptions;

        public UserService(IOptions<AuthOptions> options, TimeProvider? timeProvider = null)
        {
            authOptions = options.Value;
            if (authOptions.AccessTokenLifetimeMinutes < 1 || authOptions.AccessTokenLifetimeMinutes > 1440)
                throw new ArgumentOutOfRangeException(nameof(options), "인증 토큰 기한은 1~1440분이어야 합니다.");
            this.timeProvider = timeProvider ?? TimeProvider.System;
            if (authOptions.SessionGraceMinutes < 0 || authOptions.SessionGraceMinutes > 60)
                throw new ArgumentOutOfRangeException(nameof(options), "세션 유예 시간은 0~60분이어야 합니다.");
        }

        private bool ExistsUserName(string userName)
        {
            return users.Values.Any(user
                => user.Name.Equals(userName, StringComparison.OrdinalIgnoreCase));
        }

        public UserInfo? FindById(string id)
        {
            users.TryGetValue(id, out UserInfo? user);

            return user;
        }

        private bool CreateUser(string userName, out UserInfo? user)
        {
            user = new UserInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = userName
            };

            if (!users.TryAdd(user.Id, user))
            {
                user = null;
                return false;
            }

            return true;
        }

        public LoginResponse Login(LoginRequest request)
        {
            lock (MembershipLock) return LoginCore(request);
        }

        private LoginResponse LoginCore(LoginRequest request)
        {
            // 1. 공백 검사
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = "닉네임을 입력해주세요."
                };
            }

            // 2. 닉네임 길이 검사
            if (request.UserName.Length < authOptions.NameMinLength ||
                request.UserName.Length > authOptions.NameMaxLength)
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = $"닉네임은 {authOptions.NameMinLength} ~ {authOptions.NameMaxLength}자 사이여야 합니다."
                };
            }

            // 3. 중복 검사
            if (!authOptions.AllowDuplicateName &&
                ExistsUserName(request.UserName))
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 닉네임입니다."
                };
            }

            // 4. 사용자 생성
            if (!CreateUser(request.UserName, out UserInfo? user))
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = "사용자 생성에 실패했습니다."
                };
            }

            // 5. 성공
            var session = CreateSession(user!.Id);
            return new LoginResponse
            {
                Success = true,
                Message = "로그인 성공",
                User = user,
                AccessToken = session.Token,
                ExpiresAtUtc = session.ExpiresAtUtc
            };
        }
    }
}
