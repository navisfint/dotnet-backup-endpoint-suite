using VaultGuard.Common.Util;
using VaultGuard.Endpoint.Agent;
using VaultGuard.Endpoint.Agent.Logging;

// ============================================================================
//  终端安全准入与行为审计系统 —— 终端代理
//  形态：同一份程序既可控制台直接运行（dotnet run），也可注册为 Windows 服务
// ============================================================================

var builder = Host.CreateApplicationBuilder(args);

// 以 Windows 服务方式运行时工作目录为 System32，
// AddWindowsService 会把内容根切换到程序目录，保证 appsettings.json 与 data 目录定位正确
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "VaultGuard Endpoint Agent";
});

// 运行参数（appsettings.json 根级）：ServerUrl / HostName
builder.Services.AddOptions<AgentOptions>().Bind(builder.Configuration);

// 运行日志同时写入程序目录 data/logs，便于服务形态下排查问题
builder.Logging.AddProvider(new FileLoggerProvider(AppPath.LogDir));

// ---- 业务组件 ----
builder.Services.AddSingleton<AgentStateStore>();   // 终端身份（令牌）持久化
builder.Services.AddSingleton<PolicyEnforcer>();    // 注册表管控策略执行
builder.Services.AddSingleton<FileAuditMonitor>();  // 文件操作审计监控

// 与控制端通信的 HttpClient 由工厂统一管理（超时、基地址在 ServerClient 内设置）
builder.Services.AddHttpClient<ServerClient>();

// 主工作循环：注册 → 心跳 → 策略执行 → 事件上报
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
