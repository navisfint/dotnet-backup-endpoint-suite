namespace VaultGuard.Endpoint.Server.Services;

/// <summary>终端注册请求体（POST /api/agent/register）。</summary>
public sealed class RegisterRequest
{
    /// <summary>终端计算机名，作为终端对象的唯一标识。</summary>
    public string HostName { get; set; } = "";

    /// <summary>终端 IP 地址。</summary>
    public string IpAddress { get; set; } = "";

    /// <summary>操作系统版本描述。</summary>
    public string OsVersion { get; set; } = "";

    /// <summary>终端代理程序版本。</summary>
    public string AgentVersion { get; set; } = "V1.0";
}

/// <summary>终端注册响应体。控制端把令牌下发给终端，终端据此进行后续通信。</summary>
public sealed class RegisterResponse
{
    /// <summary>终端在控制端的唯一编号。</summary>
    public long AgentId { get; set; }

    /// <summary>通信令牌，终端需持久化保存。</summary>
    public string Token { get; set; } = "";

    /// <summary>该终端是否被允许接入管理。</summary>
    public bool Allowed { get; set; } = true;
}

/// <summary>心跳响应体：控制端把该终端当前生效的策略回传给终端。</summary>
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

    /// <summary>需要审计的文件目录列表。</summary>
    public List<string> AuditPaths { get; set; } = new();
}

/// <summary>终端上报的单个文件操作审计事件。</summary>
public sealed class AuditEventDto
{
    /// <summary>事件类型：新建 / 修改 / 删除 / 重命名。</summary>
    public string EventType { get; set; } = "";

    /// <summary>发生变化的文件或目录全路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>重命名事件的目标路径。</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>触发操作的登录用户名（终端尽力获取）。</summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 事件在终端上的发生时间。以字符串接收，兼容 ISO 8601
    /// （<c>2026-10-15T18:50:00</c>）与 <c>2026-10-15 18:50:00</c> 两种写法；
    /// 解析失败或为空时由控制端以接收时间兜底。
    /// </summary>
    public string? OccurredAt { get; set; }
}
