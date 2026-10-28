namespace VaultGuard.Common.Models;

/// <summary>单个文件在备份仓库中的索引条目，序列化后形成快照清单（manifest）。</summary>
public class FileEntry
{
    /// <summary>相对备份根的路径，统一使用 '\' 分隔。</summary>
    public string RelPath { get; set; } = "";

    public long Length { get; set; }

    /// <summary>最后写入时间（UTC 刻度），用于增量比对。</summary>
    public long LastWriteUtcTicks { get; set; }

    /// <summary>内容 CRC32，用于在时间戳变化时确认内容是否真的变了。</summary>
    public uint Crc32 { get; set; }

    /// <summary>在仓库中的存放相对路径；增量备份中未复制的文件为空（沿用历史副本）。</summary>
    public string? RepoRelPath { get; set; }

    /// <summary>该条目实际由哪一个备份点写入仓库。</summary>
    public long SourcePointId { get; set; }
}

/// <summary>一次备份执行的记录（备份点）。</summary>
public class BackupPointRecord
{
    public long Id { get; set; }

    public long JobId { get; set; }

    /// <summary>执行时的任务名称快照，便于任务改名后回溯。</summary>
    public string JobName { get; set; } = "";

    /// <summary>来源主机名称快照。</summary>
    public string HostName { get; set; } = "";

    public BackupMode Mode { get; set; } = BackupMode.Full;

    /// <summary>数据来源快照。</summary>
    public string Sources { get; set; } = "";

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime? FinishedAt { get; set; }

    /// <summary>成功 / 失败 / 进行中。</summary>
    public string Status { get; set; } = "进行中";

    /// <summary>本次实际复制到仓库的文件数。</summary>
    public int CopiedCount { get; set; }

    /// <summary>本次实际复制的字节数。</summary>
    public long CopiedBytes { get; set; }

    /// <summary>逻辑文件总数（含沿用历史副本的条目）。</summary>
    public int TotalCount { get; set; }

    /// <summary>逻辑总字节数。</summary>
    public long TotalBytes { get; set; }

    /// <summary>快照清单文件路径。</summary>
    public string ManifestPath { get; set; } = "";

    /// <summary>失败原因或备注。</summary>
    public string Message { get; set; } = "";

    /// <summary>耗时（秒）。</summary>
    public double ElapsedSeconds { get; set; }
}
