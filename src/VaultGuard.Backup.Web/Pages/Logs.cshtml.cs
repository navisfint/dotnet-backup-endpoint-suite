using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 操作日志页。展示系统的全部管理动作（登录、主机与任务的增删改、连通性检测、备份与还原），
/// 按时间倒序分页呈现，用于审计与自查。
/// </summary>
public class LogsModel : PageModel
{
    /// <summary>每页条数。</summary>
    public const int PageSize = 50;

    private readonly BackupRepository _repository;

    /// <summary>构造操作日志页模型。</summary>
    public LogsModel(BackupRepository repository)
    {
        _repository = repository;
    }

    /// <summary>当前页的日志行。</summary>
    public List<Dictionary<string, object?>> Logs { get; private set; } = new();

    /// <summary>日志总数。</summary>
    public int Total { get; private set; }

    /// <summary>当前页码。</summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>总页数。</summary>
    public int TotalPages { get; private set; } = 1;

    /// <summary>打开操作日志页。</summary>
    /// <param name="pageIndex">页码，从 1 开始。</param>
    public void OnGet(int? pageIndex)
    {
        var page = pageIndex ?? 1;
        var (rows, total) = _repository.QueryLogs(page, PageSize);

        Total = total;
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        CurrentPage = Math.Clamp(page, 1, TotalPages);

        // 页码越界时按修正后的页码再取一次，保证列表与页码一致
        if (CurrentPage != page)
        {
            (rows, total) = _repository.QueryLogs(CurrentPage, PageSize);
            Total = total;
        }

        Logs = rows;
    }
}
