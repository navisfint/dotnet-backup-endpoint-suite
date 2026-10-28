using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Endpoint.Server.Services;

namespace VaultGuard.Endpoint.Server.Pages;

/// <summary>
/// 策略配置页（路由 /Policies/{agentId}）。针对单台终端配置
/// "禁用控制面板""禁用 USB 存储""文件审计目录"三项，保存即下发，
/// 终端在下一次心跳（最长 30 秒）后拉取并执行。
/// </summary>
public class PoliciesModel : PageModel
{
    private readonly EndpointRepository _repository;

    /// <summary>构造策略配置页模型。</summary>
    public PoliciesModel(EndpointRepository repository)
    {
        _repository = repository;
    }

    /// <summary>目标终端编号（来自路由）。</summary>
    [BindProperty(SupportsGet = true)]
    public long AgentId { get; set; }

    /// <summary>是否禁用控制面板。</summary>
    [BindProperty]
    public bool NoControlPanel { get; set; }

    /// <summary>是否禁用 USB 存储设备。</summary>
    [BindProperty]
    public bool DisableUsb { get; set; }

    /// <summary>需要审计的目录，每行一个。</summary>
    [BindProperty]
    public string AuditPathsText { get; set; } = "";

    /// <summary>目标终端主机名。</summary>
    public string HostName { get; private set; } = "";

    /// <summary>目标终端 IP 与系统版本等摘要信息。</summary>
    public string OsVersion { get; private set; } = "";

    /// <summary>目标终端最后心跳时间文本。</summary>
    public string LastSeen { get; private set; } = "";

    /// <summary>策略最近一次更新时间。</summary>
    public string UpdatedAt { get; private set; } = "";

    /// <summary>策略最近一次被终端拉取的时间。</summary>
    public string AppliedAt { get; private set; } = "";

    /// <summary>操作结果提示。</summary>
    public string? Message { get; private set; }

    /// <summary>打开策略配置页，载入终端当前的策略值。</summary>
    public IActionResult OnGet()
    {
        if (!LoadAgent())
        {
            return RedirectToPage("/Agents");
        }

        var policy = _repository.GetPolicy(AgentId);
        if (policy is not null)
        {
            NoControlPanel = Row.Bool(policy, "no_control_panel");
            DisableUsb = Row.Bool(policy, "disable_usb");
            // 库中以分号分隔存储，界面上按行展示便于编辑
            AuditPathsText = string.Join(Environment.NewLine,
                EndpointRepository.SplitPaths(Row.Str(policy, "audit_paths")));
            UpdatedAt = Row.Str(policy, "updated_at");
            AppliedAt = Row.Str(policy, "applied_at");
        }

        return Page();
    }

    /// <summary>保存并下发策略。</summary>
    public IActionResult OnPost()
    {
        if (!LoadAgent())
        {
            return RedirectToPage("/Agents");
        }

        var paths = EndpointRepository.SplitPaths(AuditPathsText);
        _repository.SavePolicy(AgentId, NoControlPanel, DisableUsb, paths);

        var detail = $"禁用控制面板={NoControlPanel}，禁用USB存储={DisableUsb}，审计目录={paths.Count}个";
        _repository.LogOperation(User.Identity?.Name ?? "未知", "下发策略", HostName, detail);

        Message = "策略已保存并下发。终端将在下一次心跳（最长 30 秒）后拉取生效。";

        // 重新读取策略的更新时间，保证页面显示与库内状态一致
        var policy = _repository.GetPolicy(AgentId);
        if (policy is not null)
        {
            UpdatedAt = Row.Str(policy, "updated_at");
            AppliedAt = Row.Str(policy, "applied_at");
        }

        return Page();
    }

    /// <summary>载入目标终端信息；终端不存在时返回 false。</summary>
    private bool LoadAgent()
    {
        var agent = _repository.GetAgent(AgentId);
        if (agent is null)
        {
            return false;
        }

        HostName = Row.Str(agent, "hostname");
        OsVersion = Row.Str(agent, "os_version");
        LastSeen = Row.Str(agent, "last_seen_at");
        return true;
    }
}
