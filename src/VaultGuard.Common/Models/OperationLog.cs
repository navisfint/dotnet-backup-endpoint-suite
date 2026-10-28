namespace VaultGuard.Common.Models;

/// <summary>系统操作日志（审计用）。</summary>
public class OperationLog
{
    public long Id { get; set; }

    /// <summary>操作人。</summary>
    public string Actor { get; set; } = "";

    /// <summary>动作，例如"新建主机""执行备份""还原文件"。</summary>
    public string Action { get; set; } = "";

    /// <summary>操作对象，例如主机名、任务名。</summary>
    public string Target { get; set; } = "";

    /// <summary>补充说明。</summary>
    public string Detail { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
