namespace VaultGuard.Common.Models;

/// <summary>
/// 被管理的 Windows 主机对象（对应功能需求"使用账号密码添加 Windows 主机对象"）。
/// 密码以密文（AES-256-GCM）存储，明文不落库、不进日志。
/// </summary>
public class HostRecord
{
    public long Id { get; set; }

    /// <summary>主机显示名称，例如"财务部-文件服务器"。</summary>
    public string Name { get; set; } = "";

    /// <summary>主机地址：IP、主机名或 UNC 共享路径。</summary>
    public string Address { get; set; } = "";

    /// <summary>操作系统类型标识，例如 windows。</summary>
    public string OsType { get; set; } = "windows";

    /// <summary>访问该主机所用账号。</summary>
    public string UserName { get; set; } = "";

    /// <summary>账号密码密文（Base64），由 SecretBox 加解密。</summary>
    public string PasswordCipher { get; set; } = "";

    public string Remark { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最近一次连通性检测结果。</summary>
    public string LastStatus { get; set; } = "未检测";

    public DateTime? LastCheckAt { get; set; }
}
