using System.Security.Cryptography;

namespace VaultGuard.Common.Security;

/// <summary>
/// 登录口令哈希工具。采用 PBKDF2-HMAC-SHA256，210000 次迭代，
/// 每个账号独立随机盐；数据库中不保存任何明文口令。
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    /// <summary>生成随机盐（Base64）。</summary>
    public static string NewSalt()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize));

    /// <summary>按盐计算口令哈希（Base64）。</summary>
    public static string Hash(string password, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize);
        return Convert.ToBase64String(key);
    }

    /// <summary>校验口令（定长时间比较，避免时序侧信道）。</summary>
    public static bool Verify(string password, string saltBase64, string expectedHashBase64)
    {
        if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(expectedHashBase64))
        {
            return false;
        }

        var actual = Convert.FromBase64String(Hash(password, saltBase64));
        var expected = Convert.FromBase64String(expectedHashBase64);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
