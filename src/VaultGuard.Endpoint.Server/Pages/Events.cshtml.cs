using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Endpoint.Server.Services;

namespace VaultGuard.Endpoint.Server.Pages;

/// <summary>
/// 审计日志查询页。支持按"计算机对象 + 时间区间 + 事件类型 + 路径关键字"
/// 组合筛选，结果按发生时间倒序分页展示（每页 50 条）。
/// 查询条件通过 GET 处理函数的参数从查询字符串绑定。
/// </summary>
public class EventsModel : PageModel
{
    /// <summary>每页条数。</summary>
    public const int PageSize = 50;

    private readonly EndpointRepository _repository;

    /// <summary>构造审计日志查询页模型。</summary>
    public EventsModel(EndpointRepository repository)
    {
        _repository = repository;
    }

    /// <summary>当前筛选条件：计算机名，空表示不限。</summary>
    public string? Host { get; private set; }

    /// <summary>当前筛选条件：事件类型，空表示不限。</summary>
    public string? EventType { get; private set; }

    /// <summary>当前筛选条件：路径关键字，空表示不限。</summary>
    public string? Keyword { get; private set; }

    /// <summary>当前筛选条件：起始日期（含），null 表示不限。</summary>
    public DateTime? FromDate { get; private set; }

    /// <summary>当前筛选条件：结束日期（含），null 表示不限。</summary>
    public DateTime? ToDate { get; private set; }

    /// <summary>当前页的审计事件。</summary>
    public List<Dictionary<string, object?>> Events { get; private set; } = new();

    /// <summary>可选的计算机关联列表。</summary>
    public List<string> HostOptions { get; private set; } = new();

    /// <summary>可选的事件类型列表。</summary>
    public string[] TypeOptions => EventDisplay.AllTypes;

    /// <summary>满足筛选条件的总条数。</summary>
    public int Total { get; private set; }

    /// <summary>总页数（至少为 1）。</summary>
    public int TotalPages { get; private set; } = 1;

    /// <summary>当前使用的页码（已做边界修正）。</summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>执行查询。</summary>
    /// <param name="host">计算机名。</param>
    /// <param name="type">事件类型。</param>
    /// <param name="keyword">路径关键字。</param>
    /// <param name="from">起始日期。</param>
    /// <param name="to">结束日期。</param>
    /// <param name="pageIndex">页码，从 1 开始。注意：不能命名为 page——"page" 是 Razor Pages 的保留路由键，会被框架占用而无法绑定。</param>
    public void OnGet(string? host, string? type, string? keyword, DateTime? from, DateTime? to, int pageIndex = 1)
    {
        Host = host;
        EventType = type;
        Keyword = keyword;
        FromDate = from;
        ToDate = to;

        HostOptions = _repository.ListHostNames();

        CurrentPage = pageIndex < 1 ? 1 : pageIndex;

        // 起止日期按自然日处理：起始取当天 00:00:00，结束取当天 23:59:59
        var fromTime = from?.Date;
        var toTime = to?.Date.AddDays(1).AddSeconds(-1);

        var (rows, total) = _repository.QueryEvents(host, type, keyword, fromTime, toTime, CurrentPage, PageSize);

        Total = total;
        TotalPages = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);

        // 页码越界时回退到最后一页，保证筛选条件变化后旧链接仍能正常展示
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
            (rows, _) = _repository.QueryEvents(host, type, keyword, fromTime, toTime, CurrentPage, PageSize);
        }

        Events = rows;
    }
}
