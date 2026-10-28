using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Endpoint.Server.Services;

namespace VaultGuard.Endpoint.Server.Pages;

/// <summary>
/// 首页概览。展示终端总数、在线终端数、今日审计事件数等关键指标，
/// 以及最近上报的一批审计事件，方便管理员快速掌握全局。
/// </summary>
public class IndexModel : PageModel
{
    private readonly EndpointRepository _repository;

    /// <summary>构造首页模型。</summary>
    public IndexModel(EndpointRepository repository)
    {
        _repository = repository;
    }

    /// <summary>终端总数。</summary>
    public int TotalAgents { get; private set; }

    /// <summary>当前在线终端数。</summary>
    public int OnlineAgents { get; private set; }

    /// <summary>已配置过策略的终端数。</summary>
    public int PolicyAgents { get; private set; }

    /// <summary>今日（自然日）发生的审计事件数。</summary>
    public int TodayEvents { get; private set; }

    /// <summary>今日新建 / 修改 / 删除 / 重命名事件明细计数。</summary>
    public int TodayCreated { get; private set; }
    public int TodayModified { get; private set; }
    public int TodayDeleted { get; private set; }
    public int TodayRenamed { get; private set; }

    /// <summary>最近上报的审计事件列表。</summary>
    public List<Dictionary<string, object?>> RecentEvents { get; private set; } = new();

    /// <summary>打开概览页，汇总各项统计指标。</summary>
    public void OnGet()
    {
        // 打开控制台时顺带把长时间没有心跳的终端刷成离线，避免状态长期失真
        _repository.MarkOfflineAgents(EndpointRepository.OnlineThresholdSeconds);

        var agents = _repository.ListAgents();
        TotalAgents = agents.Count;
        OnlineAgents = agents.Count(EndpointRepository.IsOnline);
        PolicyAgents = agents.Count(a => a.ContainsKey("no_control_panel") && a["no_control_panel"] is not null);

        var todayStart = DateTime.Today;
        var todayCounts = _repository.CountEventsByTypeSince(todayStart);
        TodayCreated = GetCount(todayCounts, "新建");
        TodayModified = GetCount(todayCounts, "修改");
        TodayDeleted = GetCount(todayCounts, "删除");
        TodayRenamed = GetCount(todayCounts, "重命名");
        TodayEvents = TodayCreated + TodayModified + TodayDeleted + TodayRenamed;

        RecentEvents = _repository.RecentEvents(15);
    }

    /// <summary>从统计字典中安全取值，缺失时返回 0。</summary>
    private static int GetCount(Dictionary<string, int> counts, string key)
        => counts.TryGetValue(key, out var value) ? value : 0;
}
