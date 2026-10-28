using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Endpoint.Server.Services;

namespace VaultGuard.Endpoint.Server.Pages;

/// <summary>
/// 终端列表页。展示已注册终端的主机名、IP、系统版本、在线状态、
/// 最后心跳时间与策略摘要，并提供进入策略配置页的入口。
/// </summary>
public class AgentsModel : PageModel
{
    private readonly EndpointRepository _repository;

    /// <summary>构造终端列表页模型。</summary>
    public AgentsModel(EndpointRepository repository)
    {
        _repository = repository;
    }

    /// <summary>终端列表（含策略字段）。</summary>
    public List<Dictionary<string, object?>> Agents { get; private set; } = new();

    /// <summary>打开终端列表页。</summary>
    public void OnGet()
    {
        // 先刷新离线状态，再取列表，保证页面显示与真实心跳一致
        _repository.MarkOfflineAgents(EndpointRepository.OnlineThresholdSeconds);
        Agents = _repository.ListAgents();
    }
}
