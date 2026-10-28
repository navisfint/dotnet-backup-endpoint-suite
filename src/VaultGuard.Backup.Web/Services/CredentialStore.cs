using System.Security.Cryptography;
using VaultGuard.Common.Security;
using VaultGuard.Common.Util;

namespace VaultGuard.Backup.Web.Services;

/// <summary>
/// 主机凭据加密存储。被管主机的访问口令一律经 <see cref="SecretBox"/>（AES-256-GCM）
/// 加密后再落库，数据库中只保存 Base64 密文；解密失败时给出可读提示而非直接抛异常。
/// </summary>
public sealed class CredentialStore
{
    private readonly SecretBox _box;

    /// <summary>构造凭据存储，密钥取自程序目录 data/secret.key（首次运行自动生成）。</summary>
    public CredentialStore()
    {
        _box = new SecretBox(AppPath.SecretKeyFile);
    }

    /// <summary>加密口令明文，返回 Base64 密文；空口令返回空串。</summary>
    public string Encrypt(string? plainText)
        => string.IsNullOrEmpty(plainText) ? "" : _box.Encrypt(plainText);

    /// <summary>
    /// 解密 Base64 密文。密文为空返回空串；密钥不符或密文损坏时不抛异常，
    /// 而是返回空串并交由调用方按"口令不可用"处理（见 <see cref="TryDecrypt"/>）。
    /// </summary>
    public string Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return "";
        }

        return _box.Decrypt(cipherText);
    }

    /// <summary>
    /// 尝试解密口令，失败时通过 <paramref name="error"/> 返回可读提示。
    /// 用于连通性检测、备份执行等不应因单个凭据损坏而中断整体流程的场景。
    /// </summary>
    /// <param name="cipherText">数据库中的 Base64 密文。</param>
    /// <param name="plainText">解密得到的明文，失败时为空串。</param>
    /// <param name="error">失败原因描述，成功时为空串。</param>
    /// <returns>解密是否成功。</returns>
    public bool TryDecrypt(string? cipherText, out string plainText, out string error)
    {
        plainText = "";
        error = "";

        if (string.IsNullOrEmpty(cipherText))
        {
            return true; // 无口令（使用当前登录身份），视为正常
        }

        try
        {
            plainText = _box.Decrypt(cipherText);
            return true;
        }
        catch (CryptographicException)
        {
            error = "凭据解密失败：密钥文件与数据库不匹配或密文已损坏，请重新编辑主机并录入密码。";
            return false;
        }
        catch (FormatException)
        {
            error = "凭据解密失败：数据库中的密文格式非法（非 Base64），请重新编辑主机并录入密码。";
            return false;
        }
    }
}
