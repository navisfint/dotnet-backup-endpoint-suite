using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Common.Models;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 备份任务管理页。支持列表、新增、编辑、删除与「立即执行」。
/// 任务类型可选「文件备份」或「虚拟机备份」：按设计约定，虚拟机无代理备份为有条件实现，
/// 选择该类型时页面给出明确提示并在服务端拒绝提交，不做任何伪实现。
/// </summary>
public class JobsModel : PageModel
{
    private readonly BackupRepository _repository;
    private readonly BackupEngine _engine;
    private readonly IVmBackupProvider _vmProvider;
    private readonly ILogger<JobsModel> _logger;

    /// <summary>构造任务管理页模型。</summary>
    public JobsModel(BackupRepository repository, BackupEngine engine,
        IVmBackupProvider vmProvider, ILogger<JobsModel> logger)
    {
        _repository = repository;
        _engine = engine;
        _vmProvider = vmProvider;
        _logger = logger;
    }

    /// <summary>备份任务列表（含主机名称）。</summary>
    public List<Dictionary<string, object?>> Jobs { get; private set; } = new();

    /// <summary>可选主机下拉项。</summary>
    public List<Dictionary<string, object?>> HostOptions { get; private set; } = new();

    /// <summary>虚拟机备份能力的不可用说明（供界面展示）。</summary>
    public string VmUnavailableReason => _vmProvider.UnavailableReason;

    // ---- 新增 / 编辑表单字段 ----

    /// <summary>正在编辑的任务编号；为 0 表示新增。</summary>
    [BindProperty]
    public long JobId { get; set; }

    /// <summary>任务名称。</summary>
    [BindProperty]
    public string JobName { get; set; } = "";

    /// <summary>关联主机编号。</summary>
    [BindProperty]
    public long JobHostId { get; set; }

    /// <summary>数据来源（分号分隔）。</summary>
    [BindProperty]
    public string Sources { get; set; } = "";

    /// <summary>备份仓库根目录。</summary>
    [BindProperty]
    public string RepoPath { get; set; } = "";

    /// <summary>备份模式（0 全量 / 1 差异 / 2 增量）。</summary>
    [BindProperty]
    public int Mode { get; set; }

    /// <summary>排除规则（分号分隔通配符）。</summary>
    [BindProperty]
    public string Excludes { get; set; } = "";

    /// <summary>是否启用。</summary>
    [BindProperty]
    public bool Enabled { get; set; } = true;

    /// <summary>备份类型：file（文件备份）/ vm（虚拟机备份）。</summary>
    [BindProperty]
    public string JobType { get; set; } = "file";

    /// <summary>操作结果提示（由 TempData 跨请求传递）。</summary>
    public string? Message => TempData["Message"] as string;

    /// <summary>提示类型：ok / error / warn。</summary>
    public string MessageKind => TempData["MessageKind"] as string ?? "ok";

    /// <summary>打开任务管理页；带 id 时载入该任务进入编辑状态。</summary>
    /// <param name="id">待编辑的任务编号。</param>
    public void OnGet(long? id)
    {
        Jobs = _repository.ListJobs();
        HostOptions = _repository.ListHostOptions();

        if (id.HasValue)
        {
            var job = _repository.GetJob(id.Value);
            if (job is not null)
            {
                JobId = Row.Long(job, "id");
                JobName = Row.Str(job, "name");
                JobHostId = Row.Long(job, "host_id");
                Sources = Row.Str(job, "sources");
                RepoPath = Row.Str(job, "repo_path");
                Mode = Row.Int(job, "mode");
                Excludes = Row.Str(job, "excludes");
                Enabled = Row.Bool(job, "enabled");
            }
        }
    }

    /// <summary>新增备份任务。</summary>
    public IActionResult OnPostCreate()
    {
        if (IsVmSelected())
        {
            SetMessage("warn", _vmProvider.UnavailableReason);
            return RedirectToPage("/Jobs");
        }

        if (!Validate(out var error))
        {
            SetMessage("error", error);
            return RedirectToPage("/Jobs");
        }

        var record = BuildRecord();
        var id = _repository.InsertJob(record);
        _repository.LogOperation(User.Identity?.Name ?? "", "新建任务", record.Name,
            $"{Display.ModeName(record.Mode)}备份，来源 {record.Sources}，仓库 {record.RepoPath}");

        SetMessage("ok", $"备份任务「{record.Name}」已创建（编号 {id}）");
        return RedirectToPage("/Jobs");
    }

    /// <summary>编辑备份任务。</summary>
    public IActionResult OnPostUpdate()
    {
        if (JobId <= 0)
        {
            SetMessage("error", "未指定要编辑的任务");
            return RedirectToPage("/Jobs");
        }

        if (IsVmSelected())
        {
            SetMessage("warn", _vmProvider.UnavailableReason);
            return RedirectToPage("/Jobs");
        }

        if (!Validate(out var error))
        {
            SetMessage("error", error);
            return RedirectToPage("/Jobs");
        }

        var record = BuildRecord();
        record.Id = JobId;
        _repository.UpdateJob(record);
        _repository.LogOperation(User.Identity?.Name ?? "", "编辑任务", record.Name, "更新任务配置");

        SetMessage("ok", $"备份任务「{record.Name}」已更新");
        return RedirectToPage("/Jobs");
    }

    /// <summary>删除备份任务。已有备份点时拒绝删除。</summary>
    /// <param name="id">任务编号。</param>
    public IActionResult OnPostDelete(long id)
    {
        var job = _repository.GetJob(id);
        if (job is null)
        {
            SetMessage("error", "任务不存在或已被删除");
            return RedirectToPage("/Jobs");
        }

        var name = Row.Str(job, "name");
        if (!_repository.DeleteJob(id))
        {
            SetMessage("error", $"任务「{name}」已产生备份点，为保留历史数据不允许删除");
            return RedirectToPage("/Jobs");
        }

        _repository.LogOperation(User.Identity?.Name ?? "", "删除任务", name, "删除备份任务");
        SetMessage("ok", $"备份任务「{name}」已删除");
        return RedirectToPage("/Jobs");
    }

    /// <summary>立即执行一次备份（同步执行），完成后跳转备份点列表并提示结果。</summary>
    /// <param name="id">任务编号。</param>
    public IActionResult OnPostRun(long id)
    {
        var job = _repository.GetJob(id);
        if (job is null)
        {
            SetMessage("error", "任务不存在或已被删除");
            return RedirectToPage("/Jobs");
        }

        // 引擎内部已做异常隔离：单次备份失败不会影响 Web 进程
        var result = _engine.Run(id, User.Identity?.Name ?? "system");
        _logger.LogInformation("任务 #{Job} 执行结果：{Result}", id, result.Message);

        SetMessage(result.Success ? "ok" : "error", result.Message);
        return RedirectToPage("/Points");
    }

    /// <summary>判断提交的备份类型是否为虚拟机备份。</summary>
    private bool IsVmSelected()
        => string.Equals(JobType, "vm", StringComparison.OrdinalIgnoreCase);

    /// <summary>校验任务表单，返回是否通过。</summary>
    private bool Validate(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(JobName))
        {
            error = "任务名称不能为空";
            return false;
        }

        if (JobHostId <= 0)
        {
            error = "请选择所属主机";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Sources))
        {
            error = "数据来源不能为空（可填本地目录、盘符或 UNC 共享路径，多个用分号分隔）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            error = "备份仓库目录不能为空";
            return false;
        }

        if (Mode is < 0 or > 2)
        {
            error = "备份模式非法";
            return false;
        }

        return true;
    }

    /// <summary>由表单字段构建任务记录。</summary>
    private BackupJobRecord BuildRecord() => new()
    {
        Name = JobName.Trim(),
        HostId = JobHostId,
        Sources = Sources.Trim(),
        RepoPath = RepoPath.Trim(),
        Mode = (BackupMode)Mode,
        Excludes = (Excludes ?? "").Trim(),
        Enabled = Enabled
    };

    /// <summary>写入一条跨请求提示。</summary>
    private void SetMessage(string kind, string text)
    {
        TempData["MessageKind"] = kind;
        TempData["Message"] = text;
    }
}
