namespace VaultGuard.Endpoint.Agent;

/// <summary>终端注册请求体（对应控制端 POST /api/agent/register）。</summary>
public sealed class RegisterRequest
{
    /// <summary>终端计算机名。</summary>
    public string HostName { get; set; } = "";

    /// <summary>终端 IP 地址；留空时由控制端按连接来源记录。</summary>
    public string IpAddress { get; set; } = "";

    /// <summary>操作系统版本描述。</summary>
    public string OsVersion { get; set; } = "";

    /// <summary>终端代理程序版本。</summary>
    public string AgentVersion { get; set; } = "V1.0";
}

/// <summary>终端注册响应体。</summary>
public sealed class RegisterResponse
{
    /// <summary>控制端分配的终端编号。</summary>
    public long AgentId { get; set; }

    /// <summary>通信令牌。</summary>
    public string Token { get; set; } = "";

    /// <summary>该终端是否被允许接入管理。</summary>
    public bool Allowed { get; set; } = true;
}

/// <summary>心跳响应体，承载控制端下发的当前策略。</summary>
public sealed class HeartbeatResponse
{
    /// <summary>终端编号。</summary>
    public long AgentId { get; set; }

    /// <summary>是否允许接入。</summary>
    public bool Allowed { get; set; } = true;

    /// <summary>是否禁用控制面板。</summary>
    public bool NoControlPanel { get; set; }

    /// <summary>是否禁用 USB 存储设备。</summary>
    public bool DisableUsb { get; set; }

    /// <summary>需要监控的文件目录列表。</summary>
    public List<string> AuditPaths { get; set; } = new();
}

/// <summary>上报给控制端的单条文件操作审计事件。</summary>
public sealed class AuditEventDto
{
    /// <summary>事件类型：新建 / 修改 / 删除 / 重命名。</summary>
    public string EventType { get; set; } = "";

    /// <summary>发生变化的文件或目录全路径（重命名事件中为重命名前的路径）。</summary>
    public string Path { get; set; } = "";

    /// <summary>重命名事件的目标路径（其余事件为空）。</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>触发操作的用户名（尽力获取，取不到则为空）。</summary>
    public string UserName { get; set; } = "";

    /// <summary>事件在终端上的发生时间。</summary>
    public DateTime OccurredAt { get; set; } = DateTime.Now;
}
