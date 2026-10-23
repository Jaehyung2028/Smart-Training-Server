using System.Security.Cryptography;
using Smart_Training_Server.Models;

// 방 비밀번호의 형식 검사·해시 저장·일치 검사를 모읍니다. 원문을 저장하지 않고 PBKDF2를 사용합니다.
namespace Smart_Training_Server.Services.Room
{
    internal static class RoomPassword
    {
        // PBKDF2 반복 횟수입니다. 단순 해시보다 계산 비용을 높여 비밀번호 추측 비용을 늘립니다.
        private const int Iterations = 100000;

        // 비밀번호 없음은 허용하고, 설정하는 비밀번호는 공백만이 아니며 4~32자인지 확인합니다.
        public static bool Valid(string? password)
        {
            return string.IsNullOrEmpty(password) || (password.Length is >= 4 and <= 32 && !string.IsNullOrWhiteSpace(password));
        }

        // 빈 비밀번호면 해제하고, 그 외에는 새 salt와 PBKDF2 해시를 만들어 방 내부에 보관합니다.
        public static void Set(RoomInfo room, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                room.PasswordSalt = Array.Empty<byte>();
                room.PasswordHash = Array.Empty<byte>();
                return;
            }

            room.PasswordSalt = RandomNumberGenerator.GetBytes(16);
            room.PasswordHash = Rfc2898DeriveBytes.Pbkdf2(password, room.PasswordSalt, Iterations, HashAlgorithmName.SHA256, 32);
        }

        // 입력 비밀번호를 같은 salt로 해시해 저장값과 비교합니다. 비교 시간 차이를 줄이기 위해 FixedTimeEquals를 사용합니다.
        public static bool Matches(RoomInfo room, string? password)
        {
            if (!room.HasPassword)
            {
                return true;
            }

            if (string.IsNullOrEmpty(password) || !Valid(password))
            {
                return false;
            }

            var hash = Rfc2898DeriveBytes.Pbkdf2(password, room.PasswordSalt, Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(room.PasswordHash, hash);
        }
    }
}
