namespace VaultGuard.Common.Models;

/// <summary>下发给终端的管控与审计策略。</summary>
public class PolicyRecord
{
    public long Id { get; set; }

    /// <summary>关联终端 Id。</summary>
    public long AgentId { get; set; }

    /// <summary>禁用控制面板（写入 HKCU\...\Policies\Explorer\NoControlPanel）。</summary>
    public bool NoControlPanel { get; set; }

    /// <summary>禁用 USB 存储设备（写入 HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR\Start）。</summary>
    public bool DisableUsbStorage { get; set; }

    /// <summary>需要审计的目录列表，分号分隔。</summary>
    public string AuditPaths { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>最近一次下发时间。</summary>
    public DateTime? AppliedAt { get; set; }
}
