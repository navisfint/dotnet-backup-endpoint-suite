using System.Runtime.InteropServices;

namespace VaultGuard.Backup.Web.Services;

/// <summary>
/// Windows 网络共享凭据会话管理（P/Invoke mpr.dll）。
/// 当数据来源是远程 UNC 路径（如 <c>\\192.168.1.20\share</c>）时，先以主机对象上
/// 保存的账号口令调用 <see cref="WNetAddConnection2"/> 建立会话，备份完成后再用
/// <see cref="WNetCancelConnection2"/> 断开，避免长期占用远程连接。
/// </summary>
public static class NetworkShare
{
    // ---- Win32 常量 ----
    private const int ResourceTypeDisk = 0x00000001;   // RESOURCETYPE_DISK：磁盘资源
    private const int NoError = 0;                     // ERROR_SUCCESS

    /// <summary>判断路径是否为 UNC 网络共享路径（以两个反斜杠开头）。</summary>
    public static bool IsUncPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.TrimStart().StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// 从 UNC 路径中提取共享根（形如 <c>\\server\share</c>）。
    /// <c>WNetAddConnection2</c> 应针对共享根建立连接，而不是深层子目录。
    /// 若路径本身不含共享名则返回原路径。
    /// </summary>
    public static string ExtractShareRoot(string uncPath)
    {
        var trimmed = uncPath.Trim().TrimEnd('\\', '/');
        var parts = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return @"\\" + parts[0] + @"\" + parts[1];
        }

        return trimmed;
    }

    /// <summary>网络资源描述结构（对应 Win32 NETRESOURCE）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NetResource
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    /// <summary>
    /// 建立到指定 UNC 共享的凭据会话。成功返回一个可释放的会话对象，
    /// 使用完毕后应 <c>using</c> 或手动 <see cref="NetworkSession.Dispose"/> 断开。
    /// </summary>
    /// <param name="uncPath">目标 UNC 路径（会取其共享根建立连接）。</param>
    /// <param name="userName">访问账号；为空表示使用当前登录身份。</param>
    /// <param name="password">访问口令明文。</param>
    /// <returns>凭据会话。</returns>
    /// <exception cref="InvalidOperationException">连接失败时抛出，附带可读的 Win32 错误信息。</exception>
    public static NetworkSession Connect(string uncPath, string? userName, string? password)
    {
        var shareRoot = ExtractShareRoot(uncPath);
        var remotePtr = Marshal.StringToHGlobalUni(shareRoot);

        try
        {
            var resource = new NetResource
            {
                dwType = ResourceTypeDisk,
                lpRemoteName = remotePtr
            };

            // flags = 0：不写入用户配置（不持久化映射，进程退出即释放）
            var code = WNetAddConnection2(ref resource, password, userName, 0);
            if (code != NoError)
            {
                throw new InvalidOperationException(
                    $"建立网络共享会话失败（{shareRoot}），Win32 错误码 {code}：{DescribeError(code)}");
            }

            return new NetworkSession(shareRoot);
        }
        finally
        {
            // 结构体已被系统复制，托管侧的非托管字符串可以立即释放
            Marshal.FreeHGlobal(remotePtr);
        }
    }

    /// <summary>把常见的 Win32 网络错误码翻译为中文提示。</summary>
    private static string DescribeError(int code) => code switch
    {
        67 => "找不到网络名（共享不存在或服务未启动）",
        86 => "指定的网络密码不正确",
        1219 => "同一服务器存在冲突的凭据连接（已用其它账号连接过）",
        1326 => "用户名或密码错误",
        53 => "找不到网络路径（主机不可达）",
        _ => "系统返回未翻译错误，请检查地址与凭据"
    };

    /// <summary>断开指定 UNC 共享的会话；断开失败不抛出异常（尽力而为）。</summary>
    private static void Disconnect(string shareRoot)
    {
        // force = true：即使仍有打开的文件也强制断开
        WNetCancelConnection2(shareRoot, 0, true);
    }

    /// <summary>
    /// 一次网络共享凭据会话，<see cref="Dispose"/> 时自动调用
    /// <see cref="WNetCancelConnection2"/> 断开连接。
    /// </summary>
    public sealed class NetworkSession : IDisposable
    {
        private bool _disposed;

        /// <summary>已建立连接的共享根路径（如 <c>\\server\share</c>）。</summary>
        public string ShareRoot { get; }

        internal NetworkSession(string shareRoot)
        {
            ShareRoot = shareRoot;
        }

        /// <summary>断开网络会话。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disconnect(ShareRoot);
        }
    }
}
