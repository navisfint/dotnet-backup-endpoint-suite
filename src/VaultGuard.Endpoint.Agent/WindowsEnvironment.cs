using System.Security.Principal;
using Microsoft.Win32;

namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// Windows 环境信息辅助。本类所有方法都不会抛异常：
/// 取不到信息时回退到 .NET 通用 API 或返回默认值，避免因环境差异导致代理退出。
/// </summary>
public static class WindowsEnvironment
{
    /// <summary>
    /// 尽力获取当前进程所属的用户名，格式为"域\用户名"。
    /// 说明：终端代理作为服务运行时，该值为服务运行账号（如 NT AUTHORITY\SYSTEM），
    /// 并非真正执行文件操作的用户，因此该字段在服务形态下仅供参考。
    /// </summary>
    public static string CurrentUserName()
    {
        try
        {
            return WindowsIdentity.GetCurrent().Name;
        }
        catch
        {
            try
            {
                return $"{Environment.UserDomainName}\\{Environment.UserName}";
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// 获取操作系统版本描述，形如 <c>Windows 10 专业版 (10.0.19045)</c>。
    /// 优先读取注册表中的产品名称，读不到时回退为通用描述。
    /// </summary>
    public static string DescribeOs()
    {
        var version = Environment.OSVersion.Version;
        var versionText = $"{version.Major}.{version.Minor}.{version.Build}";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            if (!string.IsNullOrWhiteSpace(productName))
            {
                return $"{productName.Trim()} ({versionText})";
            }
        }
        catch
        {
            // 注册表不可读时使用通用名称
        }

        return $"Windows ({versionText})";
    }
}
