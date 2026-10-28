using VaultGuard.Common.Models;

namespace VaultGuard.Backup.Web.Services;

/// <summary>
/// 主机连通性检测。对本地目录直接判断 <see cref="Directory.Exists(string)"/>；
/// 对 UNC 共享路径则先以主机对象上保存的账号口令建立网络会话，再判断共享是否可达，
/// 检测完成后立即断开。检测结果由调用方回写到 hosts.last_status / last_check_at。
/// </summary>
public sealed class HostChecker
{
    private readonly CredentialStore _credentials;

    /// <summary>构造连通性检测器。</summary>
    public HostChecker(CredentialStore credentials)
    {
        _credentials = credentials;
    }

    /// <summary>检测结果。</summary>
    /// <param name="Online">是否可达。</param>
    /// <param name="Detail">说明文本，失败时承载可读原因。</param>
    public readonly record struct CheckResult(bool Online, string Detail);

    /// <summary>对给定主机执行一次连通性检测。</summary>
    /// <param name="host">主机对象。</param>
    public CheckResult Check(HostRecord host)
    {
        var address = (host.Address ?? "").Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            return new CheckResult(false, "主机地址为空");
        }

        // 1) UNC 共享：先建立凭据会话再判断
        if (NetworkShare.IsUncPath(address))
        {
            return CheckUnc(host, address);
        }

        // 2) 本地目录 / 盘符：直接判断目录是否存在
        if (Path.IsPathRooted(address))
        {
            var ok = Directory.Exists(address);
            return new CheckResult(ok, ok ? $"本地目录可达：{address}" : $"本地目录不存在：{address}");
        }

        // 3) 纯主机名 / IP：尝试探测其默认管理共享（仅作可达性参考）
        var probe = @"\\" + address;
        var reachable = Directory.Exists(probe);
        return new CheckResult(reachable,
            reachable ? $"主机可达：{address}" : $"无法访问主机：{address}（请确认地址或改用 UNC 共享路径）");
    }

    /// <summary>对 UNC 共享执行带凭据的连通性检测。</summary>
    private CheckResult CheckUnc(HostRecord host, string address)
    {
        var hasCredential = !string.IsNullOrWhiteSpace(host.UserName) || !string.IsNullOrWhiteSpace(host.PasswordCipher);

        // 未配置凭据：按当前进程身份直接探测
        if (!hasCredential)
        {
            var ok = Directory.Exists(address);
            return new CheckResult(ok,
                ok ? $"共享可达（当前身份）：{address}" : $"共享不可达：{address}（未配置访问凭据）");
        }

        if (!_credentials.TryDecrypt(host.PasswordCipher, out var password, out var error))
        {
            return new CheckResult(false, error);
        }

        try
        {
            using var session = NetworkShare.Connect(address, host.UserName, password);
            var ok = Directory.Exists(address);
            return new CheckResult(ok, ok ? $"共享可达：{address}" : $"共享已连接但目录不可访问：{address}");
        }
        catch (InvalidOperationException ex)
        {
            return new CheckResult(false, ex.Message);
        }
    }
}
