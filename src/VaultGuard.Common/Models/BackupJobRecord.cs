namespace VaultGuard.Common.Models;

/// <summary>备份策略类型（对应功能需求"增量备份"）。</summary>
public enum BackupMode
{
    /// <summary>全量备份：复制全部选中内容，形成独立完整副本。</summary>
    Full = 0,

    /// <summary>差异备份：只复制相对最近一次全量备份发生变化的文件。</summary>
    Differential = 1,

    /// <summary>增量备份：只复制相对最近一次任意成功备份发生变化的文件。</summary>
    Incremental = 2
}

/// <summary>备份任务定义。</summary>
public class BackupJobRecord
{
    public long Id { get; set; }

    /// <summary>任务名称，例如"财务资料每日备份"。</summary>
    public string Name { get; set; } = "";

    /// <summary>关联主机 Id（hosts 表）。</summary>
    public long HostId { get; set; }

    /// <summary>数据来源：本地路径、盘符或 UNC 共享路径；多个用分号分隔。</summary>
    public string Sources { get; set; } = "";

    /// <summary>备份仓库根目录。</summary>
    public string RepoPath { get; set; } = "";

    /// <summary>备份模式。</summary>
    public BackupMode Mode { get; set; } = BackupMode.Full;

    /// <summary>排除规则（通配符，分号分隔），例如 *.tmp;*.bak。</summary>
    public string Excludes { get; set; } = "";

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
