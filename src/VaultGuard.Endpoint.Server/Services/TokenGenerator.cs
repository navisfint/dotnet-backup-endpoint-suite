using System.Security.Cryptography;

namespace VaultGuard.Endpoint.Server.Services;

/// <summary>
/// 终端代理令牌生成器。令牌在终端注册时下发，之后每次心跳与事件上报
/// 都通过 HTTP 头 <c>X-Agent-Token</c> 携带，控制端据此识别终端身份。
/// </summary>
public static class TokenGenerator
{
    /// <summary>令牌字节数（24 字节 = 48 位十六进制字符）。</summary>
    private const int TokenBytes = 24;

    /// <summary>生成一个新的随机令牌（小写十六进制字符串）。</summary>
    public static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes)).ToLowerInvariant();
}
