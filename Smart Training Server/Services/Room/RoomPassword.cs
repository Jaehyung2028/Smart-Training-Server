using System.Security.Cryptography;
using Smart_Training_Server.Models;

namespace Smart_Training_Server.Services.Room;
internal static class RoomPassword
{
    private const int Iterations = 100000;
    public static bool Valid(string? password) => string.IsNullOrEmpty(password) ||
        (password.Length is >= 4 and <= 32 && !string.IsNullOrWhiteSpace(password));
    public static void Set(RoomInfo room, string? password)
    {
        if (string.IsNullOrEmpty(password))
        { room.PasswordSalt = Array.Empty<byte>(); room.PasswordHash = Array.Empty<byte>(); return; }
        room.PasswordSalt = RandomNumberGenerator.GetBytes(16);
        room.PasswordHash = Rfc2898DeriveBytes.Pbkdf2(password, room.PasswordSalt, Iterations, HashAlgorithmName.SHA256, 32);
    }
    public static bool Matches(RoomInfo room, string? password)
    {
        if (!room.HasPassword) return true;
        if (string.IsNullOrEmpty(password) || !Valid(password)) return false;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, room.PasswordSalt, Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(room.PasswordHash, hash);
    }
}
