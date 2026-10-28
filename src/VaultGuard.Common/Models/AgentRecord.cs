namespace VaultGuard.Common.Models;

/// <summary>被管终端（系统二 Agent 注册信息）。</summary>
public class AgentRecord
{
    public long Id { get; set; }

    /// <summary>终端计算机名。</summary>
    public string HostName { get; set; } = "";

    /// <summary>终端 IP 地址。</summary>
    public string IpAddress { get; set; } = "";

    /// <summary>操作系统版本描述。</summary>
    public string OsVersion { get; set; } = "";

    /// <summary>Agent 标识令牌，用于接口鉴权。</summary>
    public string AgentToken { get; set; } = "";

    /// <summary>Agent 程序版本。</summary>
    public string AgentVersion { get; set; } = "V1.0";

    /// <summary>最近心跳时间。</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>在线 / 离线。</summary>
    public string Status { get; set; } = "离线";

    /// <summary>是否被允许接入。</summary>
    public bool Allowed { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
