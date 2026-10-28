using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using VaultGuard.Common.Data;
using VaultGuard.Common.Util;
using VaultGuard.Backup.Web.Services;

// ============================================================================
//  虚拟化数据备份还原系统 —— B/S 控制台（系统一）
//  形态：ASP.NET Core Razor Pages 管理控制台
//  默认端口：http://0.0.0.0:5081（本机 localhost:5081，局域网用服务器IP:5081）
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// 允许中文直接输出到页面（默认编码器会把中文转义成 &#x 实体，影响页面源码可读性）；
// HTML 敏感字符（< > & " '）仍会被正常转义，不存在注入风险
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// ---------------------------------------------------------------------------
//  数据访问：备份还原库（位于程序目录 data 子目录，与系统二的终端安全库相互独立）
// ---------------------------------------------------------------------------
// 控制台默认监听 5081 端口，并绑定所有网卡：本机可用 localhost 访问，
// 局域网内其他电脑也可用服务器 IP 访问（演示与复核通常从办公电脑打开）。
// 如果用户显式指定了监听地址（命令行 --urls 参数或环境变量 ASPNETCORE_URLS），
// 则以用户指定为准，避免显式参数被这里的默认值覆盖。
var explicitUrls = builder.Configuration["urls"]
                   ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(explicitUrls))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5081");
}

var database = new Database(AppPath.BackupDb);
database.EnsureSchema(BackupSchema.Ddl); // 幂等建表，重复启动不会报错
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<BackupRepository>();

// 凭据加密（AES-256-GCM）与备份引擎
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<BackupEngine>();

// 主机连通性检测
builder.Services.AddSingleton<HostChecker>();

// 虚拟机无代理备份：按设计约定仅预留接口，未配置环境时返回不适用提示
builder.Services.AddSingleton<IVmBackupProvider, VmBackupProviderNotConfigured>();

// ---------------------------------------------------------------------------
//  登录认证：基于 Cookie 的简单会话，账号口令取自 users 表
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "vaultguard_backup_auth";
        options.Cookie.HttpOnly = true;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddRazorPages(options =>
{
    // 控制台所有页面默认要求登录，未登录时由 Cookie 中间件跳转到 /Login
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

// ---------------------------------------------------------------------------
//  首次启动播种默认管理员账号（users 表为空时才执行）
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<BackupRepository>();
    if (!repository.HasAnyUser())
    {
        repository.CreateUser("admin", "VaultGuard@2026", "系统管理员");
        repository.LogOperation("system", "初始化", "admin", "首次启动自动创建默认管理员账号");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
