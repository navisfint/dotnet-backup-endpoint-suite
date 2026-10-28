namespace VaultGuard.Common.Util;

/// <summary>
/// 应用数据目录解析。两套系统的数据库、密钥、仓库、日志统一放在
/// 程序目录下的 data 子目录中，便于整包拷贝部署。
/// </summary>
public static class AppPath
{
    /// <summary>程序运行目录。</summary>
    public static string Root => AppContext.BaseDirectory;

    /// <summary>数据根目录（不存在时自动创建）。</summary>
    public static string DataDir => Ensure(Path.Combine(Root, "data"));

    /// <summary>日志目录。</summary>
    public static string LogDir => Ensure(Path.Combine(Root, "data", "logs"));

    /// <summary>系统一数据库文件。</summary>
    public static string BackupDb => Path.Combine(DataDir, "vaultguard-backup.db");

    /// <summary>系统二数据库文件。</summary>
    public static string EndpointDb => Path.Combine(DataDir, "vaultguard-endpoint.db");

    /// <summary>凭据加密密钥文件。</summary>
    public static string SecretKeyFile => Path.Combine(DataDir, "secret.key");

    /// <summary>确保目录存在并返回路径。</summary>
    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>把相对路径规范化并解析为绝对路径（相对于给定基准目录）。</summary>
    public static string Resolve(string baseDir, string relative)
        => Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(baseDir, relative));
}
