namespace VaultGuard.Common.Models;

/// <summary>文件还原操作记录（对应功能需求"还原文件"）。</summary>
public class RestoreRecord
{
    public long Id { get; set; }

    /// <summary>来源备份点 Id。</summary>
    public long PointId { get; set; }

    /// <summary>还原的文件相对路径，多个以分号分隔。</summary>
    public string Files { get; set; } = "";

    /// <summary>还原目标目录；为空表示还原到原始位置。</summary>
    public string Target { get; set; } = "";

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    /// <summary>执行人（控制台登录用户）。</summary>
    public string Operator { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Status { get; set; } = "成功";
}
