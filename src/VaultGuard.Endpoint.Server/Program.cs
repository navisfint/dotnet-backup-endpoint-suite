using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using VaultGuard.Common.Data;
using VaultGuard.Common.Util;
using VaultGuard.Endpoint.Server.Services;

// ============================================================================
//  终端安全准入与行为审计系统 —— 控制端（系统二）
//  形态：ASP.NET Core Razor Pages 管理控制台 + 终端代理通信接口
//  端口：http://0.0.0.0:5082（本机 localhost:5082，局域网用服务器IP:5082）
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// 允许中文直接输出到页面（默认编码器会把中文转义成 &#x 实体，影响页面源码可读性）；
// HTML 敏感字符（< > & " '）仍会被正常转义，不存在注入风险
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// 控制端默认监听 5082 端口，绑定所有网卡，与终端代理默认配置的 ServerUrl 保持一致。
// 终端代理通常安装在**其他电脑**上，若只绑 localhost（回环地址），代理将无法连接，
// 因此默认绑定 0.0.0.0；本机访问仍可用 localhost，局域网用服务器 IP。
// 如果用户显式指定了监听地址（命令行 --urls 参数或环境变量 ASPNETCORE_URLS），
// 则以用户指定为准，避免显式参数被这里的默认值覆盖。
var explicitUrls = builder.Configuration["urls"]
                   ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(explicitUrls))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5082");
}

// ---------------------------------------------------------------------------
//  数据访问：终端安全库（位于程序目录 data 子目录，与系统一的备份库相互独立）
// ---------------------------------------------------------------------------
var database = new Database(AppPath.EndpointDb);
database.EnsureSchema(EndpointSchema.Ddl); // 幂等建表，重复启动不会报错
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<EndpointRepository>();

// ---------------------------------------------------------------------------
//  登录认证：基于 Cookie 的简单会话，账号口令取自 users 表
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "vaultguard_endpoint_auth";
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
    var repository = scope.ServiceProvider.GetRequiredService<EndpointRepository>();
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

// ============================================================================
//  终端代理通信接口（JSON 匿名访问，全部以 X-Agent-Token 请求头做鉴权）
// ============================================================================

// ---- 终端注册：按计算机名幂等登记，下发通信令牌 ----
app.MapPost("/api/agent/register", (RegisterRequest request, EndpointRepository repository) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.HostName))
    {
        return Results.BadRequest(new { error = "hostname 不能为空" });
    }

    var hostName = request.HostName.Trim();
    var ipAddress = (request.IpAddress ?? "").Trim();
    var osVersion = (request.OsVersion ?? "").Trim();
    var agentVersion = string.IsNullOrWhiteSpace(request.AgentVersion) ? "V1.0" : request.AgentVersion.Trim();

    var (agentId, token, allowed) = repository.RegisterAgent(hostName, ipAddress, osVersion, agentVersion);
    repository.LogOperation("agent", "终端注册", hostName, $"终端 {hostName}（{ipAddress}）完成注册");

    return Results.Ok(new RegisterResponse
    {
        AgentId = agentId,
        Token = token,
        Allowed = allowed
    });
});

// ---- 心跳：刷新在线状态，并回传该终端当前生效的策略 ----
app.MapPost("/api/agent/heartbeat", (HttpRequest http, EndpointRepository repository) =>
{
    // 令牌无效或未注册时返回 401，终端据此重新注册
    var token = http.Headers["X-Agent-Token"].ToString();
    var agent = repository.FindAgentByToken(token);
    if (agent is null)
    {
        return Results.Unauthorized();
    }

    var agentId = Row.Long(agent, "id");
    var ipAddress = http.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    repository.MarkHeartbeat(agentId, ipAddress);

    var policy = repository.GetPolicy(agentId);
    var response = new HeartbeatResponse
    {
        AgentId = agentId,
        Allowed = Row.Bool(agent, "allowed"),
        NoControlPanel = policy is not null && Row.Bool(policy, "no_control_panel"),
        DisableUsb = policy is not null && Row.Bool(policy, "disable_usb"),
        AuditPaths = EndpointRepository.SplitPaths(policy is null ? "" : Row.Str(policy, "audit_paths"))
    };

    return Results.Ok(response);
});

// ---- 事件上报：批量落入 audit_events 表 ----
app.MapPost("/api/agent/events", (HttpRequest http, List<AuditEventDto>? events, EndpointRepository repository) =>
{
    var token = http.Headers["X-Agent-Token"].ToString();
    var agent = repository.FindAgentByToken(token);
    if (agent is null)
    {
        return Results.Unauthorized();
    }

    var agentId = Row.Long(agent, "id");
    var hostName = Row.Str(agent, "hostname");

    var count = repository.InsertEvents(agentId, hostName, events ?? new List<AuditEventDto>());

    // 心跳同时被刷新，避免"只在有事件时才在线"的假象
    repository.MarkHeartbeat(agentId, http.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");

    return Results.Ok(new { received = count });
});

app.Run();
