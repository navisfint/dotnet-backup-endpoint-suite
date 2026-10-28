using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Common.Models;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 主机管理页（功能需求 5：使用账号密码添加 Windows 主机对象）。
/// 支持列表、新增、编辑、删除与「连通性检测」；主机口令经 <see cref="CredentialStore"/>
/// 加密后落库，数据库中不保存明文，页面也不回显原密码。
/// </summary>
public class HostsModel : PageModel
{
    private readonly BackupRepository _repository;
    private readonly CredentialStore _credentials;
    private readonly HostChecker _checker;
    private readonly ILogger<HostsModel> _logger;

    /// <summary>构造主机管理页模型。</summary>
    public HostsModel(BackupRepository repository, CredentialStore credentials,
        HostChecker checker, ILogger<HostsModel> logger)
    {
        _repository = repository;
        _credentials = credentials;
        _checker = checker;
        _logger = logger;
    }

    /// <summary>主机对象列表。</summary>
    public List<Dictionary<string, object?>> Hosts { get; private set; } = new();

    // ---- 新增 / 编辑表单字段 ----

    /// <summary>正在编辑的主机编号；为 0 表示新增。</summary>
    [BindProperty]
    public long HostId { get; set; }

    /// <summary>主机显示名称。</summary>
    [BindProperty]
    public string HostName { get; set; } = "";

    /// <summary>主机地址：IP、主机名或 UNC 共享路径。</summary>
    [BindProperty]
    public string Address { get; set; } = "";

    /// <summary>操作系统类型标识。</summary>
    [BindProperty]
    public string OsType { get; set; } = "windows";

    /// <summary>访问账号。</summary>
    [BindProperty]
    public string UserName { get; set; } = "";

    /// <summary>访问口令；编辑时留空表示保持原密码不变。</summary>
    [BindProperty]
    public string Password { get; set; } = "";

    /// <summary>备注。</summary>
    [BindProperty]
    public string Remark { get; set; } = "";

    /// <summary>操作结果提示（由 TempData 跨请求传递）。</summary>
    public string? Message => TempData["Message"] as string;

    /// <summary>提示类型：ok / error。</summary>
    public string MessageKind => TempData["MessageKind"] as string ?? "ok";

    /// <summary>打开主机管理页；带 id 时载入该主机进入编辑状态。</summary>
    /// <param name="id">待编辑的主机编号。</param>
    public void OnGet(long? id)
    {
        Hosts = _repository.ListHosts();

        if (id.HasValue)
        {
            var host = _repository.GetHost(id.Value);
            if (host is not null)
            {
                HostId = Row.Long(host, "id");
                HostName = Row.Str(host, "name");
                Address = Row.Str(host, "address");
                OsType = string.IsNullOrWhiteSpace(Row.Str(host, "os_type")) ? "windows" : Row.Str(host, "os_type");
                UserName = Row.Str(host, "username");
                Remark = Row.Str(host, "remark");
                // 口令不回显，留空即保持原值
            }
        }
    }

    /// <summary>新增主机对象。</summary>
    public IActionResult OnPostCreate()
    {
        if (string.IsNullOrWhiteSpace(HostName) || string.IsNullOrWhiteSpace(Address))
        {
            SetMessage("error", "主机名称与地址不能为空");
            return RedirectToPage("/Hosts");
        }

        var record = new HostRecord
        {
            Name = HostName.Trim(),
            Address = Address.Trim(),
            OsType = string.IsNullOrWhiteSpace(OsType) ? "windows" : OsType.Trim(),
            UserName = (UserName ?? "").Trim(),
            PasswordCipher = _credentials.Encrypt(Password),
            Remark = (Remark ?? "").Trim(),
            LastStatus = "未检测"
        };

        var id = _repository.InsertHost(record);
        _repository.LogOperation(User.Identity?.Name ?? "", "新建主机", record.Name,
            $"地址 {record.Address}，账号 {(string.IsNullOrEmpty(record.UserName) ? "（未配置）" : record.UserName)}");

        SetMessage("ok", $"主机「{record.Name}」已创建（编号 {id}）");
        return RedirectToPage("/Hosts");
    }

    /// <summary>编辑主机对象。口令留空时保持原密码不变。</summary>
    public IActionResult OnPostUpdate()
    {
        if (HostId <= 0)
        {
            SetMessage("error", "未指定要编辑的主机");
            return RedirectToPage("/Hosts");
        }

        if (string.IsNullOrWhiteSpace(HostName) || string.IsNullOrWhiteSpace(Address))
        {
            SetMessage("error", "主机名称与地址不能为空");
            return RedirectToPage("/Hosts");
        }

        var keepPassword = string.IsNullOrEmpty(Password);
        var record = new HostRecord
        {
            Id = HostId,
            Name = HostName.Trim(),
            Address = Address.Trim(),
            OsType = string.IsNullOrWhiteSpace(OsType) ? "windows" : OsType.Trim(),
            UserName = (UserName ?? "").Trim(),
            PasswordCipher = keepPassword ? "" : _credentials.Encrypt(Password),
            Remark = (Remark ?? "").Trim()
        };

        _repository.UpdateHost(record, keepPassword);
        _repository.LogOperation(User.Identity?.Name ?? "", "编辑主机", record.Name,
            keepPassword ? "更新基本信息（口令保持不变）" : "更新基本信息并重设口令");

        SetMessage("ok", $"主机「{record.Name}」已更新" + (keepPassword ? "（口令保持不变）" : "（口令已重设）"));
        return RedirectToPage("/Hosts");
    }

    /// <summary>删除主机对象。存在关联任务时拒绝删除。</summary>
    /// <param name="id">主机编号。</param>
    public IActionResult OnPostDelete(long id)
    {
        var host = _repository.GetHost(id);
        if (host is null)
        {
            SetMessage("error", "主机不存在或已被删除");
            return RedirectToPage("/Hosts");
        }

        var name = Row.Str(host, "name");
        if (!_repository.DeleteHost(id))
        {
            SetMessage("error", $"主机「{name}」仍被备份任务引用，请先删除相关任务");
            return RedirectToPage("/Hosts");
        }

        _repository.LogOperation(User.Identity?.Name ?? "", "删除主机", name, "删除主机对象");
        SetMessage("ok", $"主机「{name}」已删除");
        return RedirectToPage("/Hosts");
    }

    /// <summary>对指定主机执行连通性检测，并回写检测结果。</summary>
    /// <param name="id">主机编号。</param>
    public IActionResult OnPostCheck(long id)
    {
        var row = _repository.GetHost(id);
        if (row is null)
        {
            SetMessage("error", "主机不存在或已被删除");
            return RedirectToPage("/Hosts");
        }

        var host = new HostRecord
        {
            Id = id,
            Name = Row.Str(row, "name"),
            Address = Row.Str(row, "address"),
            UserName = Row.Str(row, "username"),
            PasswordCipher = Row.Str(row, "password_cipher")
        };

        var result = _checker.Check(host);
        var status = result.Online ? "在线" : "离线";
        _repository.UpdateHostStatus(id, status, DateTime.Now);
        _repository.LogOperation(User.Identity?.Name ?? "", "连通性检测", host.Name, result.Detail);
        _logger.LogInformation("主机 {Host} 连通性检测：{Status}（{Detail}）", host.Name, status, result.Detail);

        SetMessage(result.Online ? "ok" : "error", $"「{host.Name}」检测结果：{status}。{result.Detail}");
        return RedirectToPage("/Hosts");
    }

    /// <summary>写入一条跨请求提示。</summary>
    private void SetMessage(string kind, string text)
    {
        TempData["MessageKind"] = kind;
        TempData["Message"] = text;
    }
}
