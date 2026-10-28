using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 概览页。展示主机数、任务数、备份点数与最近一次备份时间四张统计卡，
/// 以及最近 10 条备份记录，用于系统运行状况的快速把握。
/// </summary>
public class IndexModel : PageModel
{
    private readonly BackupRepository _repository;

    /// <summary>构造概览页模型。</summary>
    public IndexModel(BackupRepository repository)
    {
        _repository = repository;
    }

    /// <summary>主机总数。</summary>
    public long HostCount { get; private set; }

    /// <summary>备份任务总数。</summary>
    public long JobCount { get; private set; }

    /// <summary>备份点总数。</summary>
    public long PointCount { get; private set; }

    /// <summary>最近一次成功备份的时间文本，无记录时为空串。</summary>
    public string LatestBackupTime { get; private set; } = "";

    /// <summary>最近 10 条备份记录。</summary>
    public List<Dictionary<string, object?>> RecentPoints { get; private set; } = new();

    /// <summary>打开概览页并加载统计数据。</summary>
    public void OnGet()
    {
        HostCount = _repository.CountHosts();
        JobCount = _repository.CountJobs();
        PointCount = _repository.CountPoints();
        LatestBackupTime = _repository.LatestSuccessBackupTime();
        RecentPoints = _repository.RecentPoints(10);
    }
}
