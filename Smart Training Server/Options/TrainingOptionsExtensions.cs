// 설정 파일의 각 영역을 Options 객체로 연결하고 시작 시 값이 올바른지 검사합니다.
namespace Smart_Training_Server.Options
{
    public static class TrainingOptionsExtensions
    {
        // 각 설정 영역을 Options에 바인딩하고 ValidateOnStart로 잘못된 값을 서버 시작 단계에서 발견합니다.
        public static IServiceCollection AddTrainingOptions(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<OperationsOptions>().Bind(configuration.GetSection("Operations")).Validate(x => !x.PersistHistory || !string.IsNullOrWhiteSpace(x.HistoryPath), "Operations:HistoryPath를 지정해주세요.").ValidateOnStart();
            services.AddOptions<AuthOptions>().Bind(configuration.GetSection("Auth")).Validate(x => x.AccessTokenLifetimeMinutes is >= 1 and <= 1440, "Auth:AccessTokenLifetimeMinutes는 1~1440분이어야 합니다.").Validate(x => x.SessionGraceMinutes is >= 0 and <= 60, "Auth:SessionGraceMinutes는 0~60분이어야 합니다.").Validate(x => ValidNameRange(x.NameMinLength, x.NameMaxLength), "Auth: 이름 최소 길이는 1 이상이고 최대 길이 이하여야 합니다.").ValidateOnStart();
            services.AddOptions<RoomOptions>().Bind(configuration.GetSection("Room")).Validate(x => !string.IsNullOrWhiteSpace(x.ServerExecutablePath), "Room:ServerExecutablePath를 지정해주세요.").Validate(
                x => Uri.TryCreate(x.ServerApiBaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
                "Room:ServerApiBaseUrl은 인증정보·쿼리 없는 HTTP/HTTPS 주소이어야 합니다."
            ).Validate(x => x.ServerReadyTimeoutSeconds is >= 1 and <= 300, "Room:ServerReadyTimeoutSeconds는 1~300초이어야 합니다.").Validate(x => x.MinUsers >= 1 && x.MaxUsers >= x.MinUsers, "Room: 최소 인원은 1 이상이고 최대 인원 이하여야 합니다.").Validate(x => ValidNameRange(x.NameMinLength, x.NameMaxLength), "Room: 이름 최소 길이는 1 이상이고 최대 길이 이하여야 합니다.").Validate(
                x => x.AdvertisedAddress == string.Empty || (x.AdvertisedAddress != null && x.AdvertisedAddress == x.AdvertisedAddress.Trim() && Uri.CheckHostName(x.AdvertisedAddress) != UriHostNameType.Unknown && !Smart_Training_Server.Helper.NetworkHelper.IsWildcardAddress(x.AdvertisedAddress)),
                "Room:AdvertisedAddress는 빈 문자열 또는 프로토콜·포트 없는 IP/호스트 이름이어야 합니다."
            ).ValidateOnStart();
            return services;
        }

        // 이름 최소 길이가 1 이상이고 최대 길이가 그 이상인지 확인하는 공통 조건입니다.
        private static bool ValidNameRange(int minimum, int maximum)
        {
            return minimum >= 1 && maximum >= minimum;
        }
    }
}
