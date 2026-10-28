using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VaultGuard.Backup.Web.Pages;

/// <summary>退出登录页。清除认证 Cookie 后回到登录页。</summary>
public class LogoutModel : PageModel
{
    private readonly BackupRepository _repository;

    /// <summary>构造退出页模型。</summary>
    public LogoutModel(BackupRepository repository)
    {
        _repository = repository;
    }

    /// <summary>执行退出并跳转登录页。</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        var userName = User.Identity?.Name ?? "";
        if (!string.IsNullOrEmpty(userName))
        {
            // 退出动作同样留痕，便于审计追溯
            _repository.LogOperation(userName, "退出登录", "控制台", "管理用户退出控制台");
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
