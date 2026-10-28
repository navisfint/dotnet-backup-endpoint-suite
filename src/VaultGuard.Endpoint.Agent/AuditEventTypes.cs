namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// 审计事件类型常量。取值必须与控制端界面上的筛选选项保持一致，
/// 因为两端通过该字符串匹配（例如筛选"新建"事件）。
/// </summary>
public static class AuditEventTypes
{
    /// <summary>文件或目录被新建。</summary>
    public const string Created = "新建";

    /// <summary>文件或目录内容被修改。</summary>
    public const string Modified = "修改";

    /// <summary>文件或目录被删除。</summary>
    public const string Deleted = "删除";

    /// <summary>文件或目录被重命名。</summary>
    public const string Renamed = "重命名";

    /// <summary>
    /// 管控策略执行失败时上报的告警事件类型。
    /// 这类事件不属于文件操作审计，但需要通过同一条通道回报控制端。
    /// </summary>
    public const string PolicyAlert = "策略告警";
}
