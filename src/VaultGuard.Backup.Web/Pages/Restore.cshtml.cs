using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Common.Models;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 还原页（功能需求 3：还原文件）。展示某个备份点的完整文件清单（分页），
/// 勾选文件后还原：目标目录留空则还原到原始位置，填写则还原到该目录下并保留相对结构。
/// </summary>
public class RestoreModel : PageModel
{
    /// <summary>文件清单每页条数。</summary>
    public const int PageSize = 200;

    private readonly BackupRepository _repository;
    private readonly BackupEngine _engine;

    /// <summary>构造还原页模型。</summary>
    public RestoreModel(BackupRepository repository, BackupEngine engine)
    {
        _repository = repository;
        _engine = engine;
    }

    /// <summary>备份点记录，不存在时为 null。</summary>
    public Dictionary<string, object?>? Point { get; private set; }

    /// <summary>当前页的文件条目。</summary>
    public List<FileEntry> Entries { get; private set; } = new();

    /// <summary>清单文件条目总数。</summary>
    public int TotalCount { get; private set; }

    /// <summary>当前页码。</summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>总页数。</summary>
    public int TotalPages { get; private set; } = 1;

    /// <summary>勾选的文件相对路径（表单绑定）。</summary>
    [BindProperty]
    public List<string> SelectedFiles { get; set; } = new();

    /// <summary>还原目标目录；留空表示还原到原始位置。</summary>
    [BindProperty]
    public string Target { get; set; } = "";

    /// <summary>操作结果提示（由 TempData 跨请求传递）。</summary>
    public string? Message => TempData["Message"] as string;

    /// <summary>提示类型：ok / error。</summary>
    public string MessageKind => TempData["MessageKind"] as string ?? "ok";

    /// <summary>打开还原页，按分页加载指定备份点的文件清单。</summary>
    /// <param name="pointId">备份点编号（来自路由）。</param>
    /// <param name="pageIndex">页码，从 1 开始。</param>
    /// <returns>页面或未找到提示。</returns>
    public IActionResult OnGet(long pointId, int? pageIndex)
    {
        Point = _repository.GetPoint(pointId);
        if (Point is null)
        {
            return RedirectToPage("/Points");
        }

        var all = _engine.ReadManifestEntries(pointId);
        TotalCount = all.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        CurrentPage = Math.Clamp(pageIndex ?? 1, 1, TotalPages);
        Entries = all.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

        return Page();
    }

    /// <summary>执行还原，随后重定向回本页显示结果。</summary>
    /// <param name="pointId">备份点编号（来自路由）。</param>
    public IActionResult OnPost(long pointId)
    {
        var result = _engine.Restore(pointId, SelectedFiles, Target, User.Identity?.Name ?? "system");

        TempData["MessageKind"] = result.Success ? "ok" : "error";
        TempData["Message"] = result.Message;

        return RedirectToPage("/Restore", new { pointId });
    }
}
