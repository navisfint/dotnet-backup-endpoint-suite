using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 备份点列表页。按时间倒序展示全部备份点（时间、任务、模式、状态、文件数、大小、耗时），
/// 并提供进入还原页的入口。
/// </summary>
public class PointsModel : PageModel
{
    private readonly BackupRepository _repository;

    /// <summary>构造备份点列表页模型。</summary>
    public PointsModel(BackupRepository repository)
    {
        _repository = repository;
    }

    /// <summary>备份点列表（新到旧）。</summary>
    public List<Dictionary<string, object?>> Points { get; private set; } = new();

    /// <summary>操作结果提示（例如「立即执行」的回执）。</summary>
    public string? Message => TempData["Message"] as string;

    /// <summary>提示类型：ok / error / warn。</summary>
    public string MessageKind => TempData["MessageKind"] as string ?? "ok";

    /// <summary>打开备份点列表页。</summary>
    public void OnGet()
    {
        Points = _repository.ListPoints();
    }
}
