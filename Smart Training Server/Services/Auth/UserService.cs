using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Responses.Auth;
using System.Collections.Concurrent;

// 사용자 등록·조회와 현재 소속 응답을 관리합니다. 인증 토큰 발급·만료 처리는 Session 파일에 모읍니다.
namespace Smart_Training_Server.Services.Auth
{
    public partial class UserService
    {
        // 로비 이동과 방 예약/확정의 사용자 상태 변경을 함께 보호합니다.
        internal object MembershipLock { get; } = new();

        // 사용자 ID → 사용자 정보 저장소입니다. API를 다시 시작하면 메모리의 사용자 정보는 사라집니다.
        private readonly ConcurrentDictionary<string, UserInfo> users = new();
        private readonly AuthOptions authOptions;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public UserService(IOptions<AuthOptions> options, TimeProvider? timeProvider = null)
        {
            authOptions = options.Value;
            if (authOptions.AccessTokenLifetimeMinutes < 1 || authOptions.AccessTokenLifetimeMinutes > 1440)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "인증 토큰 기한은 1~1440분이어야 합니다.");
            }

            this.timeProvider = timeProvider ?? TimeProvider.System;
            if (authOptions.SessionGraceMinutes < 0 || authOptions.SessionGraceMinutes > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "세션 유예 시간은 0~60분이어야 합니다.");
            }
        }

        // 닉네임 중복은 영문 대소문자를 구분하지 않고 검사합니다.
        private bool ExistsUserName(string userName)
        {
            return users.Values.Any(user => user.Name.Equals(userName, StringComparison.OrdinalIgnoreCase));
        }

        // ID로 사용자를 찾습니다. 없으면 null이므로 호출하는 쪽에서 존재 여부를 확인해야 합니다.
        public UserInfo? FindById(string id)
        {
            users.TryGetValue(id, out UserInfo? user);
            return user;
        }

        // Guid로 새 사용자 ID를 만들어 등록합니다. 같은 닉네임 여부는 호출 전에 검사합니다.
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

        // 중복 닉네임 확인과 사용자 등록을 한 잠금 안에서 처리해 동시 로그인 경쟁을 막습니다.
        public LoginResponse Login(LoginRequest request)
        {
            lock (MembershipLock)
            {
                return LoginCore(request);
            }
        }

        // 이름 규칙과 중복을 확인한 뒤 사용자·인증 세션을 생성합니다. 호출자가 공통 소속 잠금을 보유합니다.
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
            if (request.UserName.Length < authOptions.NameMinLength || request.UserName.Length > authOptions.NameMaxLength)
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = $"닉네임은 {authOptions.NameMinLength} ~ {authOptions.NameMaxLength}자 사이여야 합니다."
                };
            }

            // 3. 중복 검사
            if (!authOptions.AllowDuplicateName && ExistsUserName(request.UserName))
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

        // 인증 사용자의 현재 소속·대기 상태를 조회 전용 응답으로 복사합니다. 사용자가 없으면 null을 반환합니다.
        public CurrentUserResponse? GetCurrentUser(string userId)
        {
            lock (MembershipLock)
            {
                var user = FindById(userId);
                return user == null ? null : new CurrentUserResponse
                {
                    Id = user.Id,
                    Name = user.Name,
                    CurrentLobbyId = user.CurrentLobbyId,
                    CurrentLobbyName = user.CurrentLobbyName,
                    LobbyIndex = user.LobbyIndex,
                    CurrentRoomId = user.CurrentRoomId,
                    CurrentRoomName = user.CurrentRoomName,
                    RoomIndex = user.RoomIndex,
                    PendingRoomId = user.PendingRoomId
                };
            }
        }
    }
}
