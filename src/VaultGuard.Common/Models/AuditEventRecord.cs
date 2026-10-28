namespace VaultGuard.Common.Models;

/// <summary>文件操作审计事件（对应功能需求"记录文件增、删、改操作"）。</summary>
public class AuditEventRecord
{
    public long Id { get; set; }

    public long AgentId { get; set; }

    /// <summary>终端计算机名快照，供"按计算机对象查询"直接使用。</summary>
    public string HostName { get; set; } = "";

    /// <summary>事件类型：新建 / 修改 / 删除 / 重命名。</summary>
    public string EventType { get; set; } = "";

    /// <summary>发生变化的文件或目录全路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>重命名事件的目标路径。</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>触发操作的登录用户名（尽力获取，取不到则为空）。</summary>
    public string UserName { get; set; } = "";

    /// <summary>事件在终端上的发生时间。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>控制端接收时间。</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
}
