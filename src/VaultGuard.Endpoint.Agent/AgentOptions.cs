namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// 终端代理运行参数，对应 appsettings.json 中的根级配置项。
/// </summary>
public sealed class AgentOptions
{
    /// <summary>
    /// 控制端地址。默认与系统二控制台的监听端口保持一致，
    /// 终端与控制端不在同一台机器时改为控制端的实际地址即可。
    /// </summary>
    public string ServerUrl { get; set; } = "http://localhost:5082";

    /// <summary>
    /// 本机计算机名。留空时自动取 <see cref="Environment.MachineName"/>，
    /// 该名称即控制台上看到的"终端对象"标识。
    /// </summary>
    public string HostName { get; set; } = "";

    /// <summary>实际生效的计算机名（HostName 为空时回退到机器名）。</summary>
    public string EffectiveHostName
        => string.IsNullOrWhiteSpace(HostName) ? Environment.MachineName : HostName.Trim();
}
