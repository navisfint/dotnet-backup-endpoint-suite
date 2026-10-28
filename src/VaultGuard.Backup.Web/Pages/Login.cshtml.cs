using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VaultGuard.Common.Security;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>
/// 登录页。控制台的唯一入口，校验 users 表中的账号口令；
/// 页面底部固定展示公司版权信息（功能需求 1：登录界面展示公司名称与版权）。
/// </summary>
public class LoginModel : PageModel
{
    private readonly BackupRepository _repository;
    private readonly ILogger<LoginModel> _logger;

    /// <summary>构造登录页模型。</summary>
    public LoginModel(BackupRepository repository, ILogger<LoginModel> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>提交的登录用户名。</summary>
    [BindProperty]
    public string UserName { get; set; } = "";

    /// <summary>提交的登录口令。</summary>
    [BindProperty]
    public string Password { get; set; } = "";

    /// <summary>登录失败时的提示信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>打开登录页；已登录用户直接跳转首页。</summary>
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    /// <summary>处理登录提交，校验通过后写入认证 Cookie。</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        var userName = (UserName ?? "").Trim();
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "请输入用户名和口令";
            return Page();
        }

        var user = _repository.FindUser(userName);
        var verified = user is not null
                       && PasswordHasher.Verify(Password, Row.Str(user, "salt"), Row.Str(user, "password_hash"));

        if (!verified)
        {
            _logger.LogWarning("登录失败：用户名 {User}", userName);
            _repository.LogOperation(userName, "登录失败", "控制台", "用户名或口令不正确");
            ErrorMessage = "用户名或口令不正确";
            return Page();
        }

        // 认证信息写入 Cookie，页面上的“当前用户”即取自此处的声明
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Row.Str(user, "username")),
            new(ClaimTypes.GivenName, Row.Str(user, "display_name"))
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        _repository.LogOperation(userName, "登录成功", "控制台", "管理用户登录控制台");
        return RedirectToPage("/Index");
    }
}
