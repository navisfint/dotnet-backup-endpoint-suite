using System.Security.Cryptography;
using System.Text;

namespace VaultGuard.Common.Security;

/// <summary>
/// 凭据加解密工具（AES-256-GCM）。
/// 系统一保存被管主机的访问密码时使用：数据库中只落密文，
/// 密钥保存在程序目录下的独立密钥文件中（首次运行自动生成）。
/// </summary>
public sealed class SecretBox
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    /// <param name="keyFilePath">密钥文件路径；文件不存在时自动创建一个 256 位随机密钥。</param>
    public SecretBox(string keyFilePath)
    {
        var path = Path.GetFullPath(keyFilePath);
        if (File.Exists(path))
        {
            var raw = File.ReadAllBytes(path);
            if (raw.Length != 32)
            {
                throw new InvalidOperationException($"密钥文件长度非法（{raw.Length} 字节），应为 32 字节：{path}");
            }
            _key = raw;
        }
        else
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(path, _key);
        }
    }

    /// <summary>加密明文，返回 Base64（Nonce + 密文 + 认证标签）。</summary>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "";
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, NonceSize + cipher.Length, TagSize);
        return Convert.ToBase64String(output);
    }

    /// <summary>解密 Base64 密文；密文为空返回空串，密钥不符将抛出 CryptographicException。</summary>
    public string Decrypt(string cipherBase64)
    {
        if (string.IsNullOrEmpty(cipherBase64))
        {
            return "";
        }

        var data = Convert.FromBase64String(cipherBase64);
        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("密文长度不足，无法解密。");
        }

        var nonce = data.AsSpan(0, NonceSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        var tag = data.AsSpan(data.Length - TagSize, TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
