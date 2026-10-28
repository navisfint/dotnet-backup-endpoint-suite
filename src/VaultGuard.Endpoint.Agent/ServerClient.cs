using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using VaultGuard.Common;

namespace VaultGuard.Endpoint.Agent;

/// <summary>一次心跳调用的结果。</summary>
public sealed class HeartbeatResult
{
    /// <summary>令牌被控制端拒绝（HTTP 401），需要重新注册。</summary>
    public bool Unauthorized { get; init; }

    /// <summary>是否成功取回策略。</summary>
    public bool Success { get; init; }

    /// <summary>控制端返回的当前策略。</summary>
    public HeartbeatResponse? Policy { get; init; }
}

/// <summary>
/// 控制端通信客户端。封装"注册 / 心跳 / 事件上报"三个接口。
/// 所有网络异常都在内部捕获并写日志，不向调用方抛出，
/// 保证控制端未启动或网络中断时终端代理仍持续运行。
/// </summary>
public sealed class ServerClient
{
    private const string TokenHeader = "X-Agent-Token";

    private readonly HttpClient _http;
    private readonly ILogger<ServerClient> _logger;

    /// <summary>构造通信客户端，并按配置初始化基地址。</summary>
    /// <param name="http">由 IHttpClientFactory 提供的 HttpClient。</param>
    /// <param name="options">终端代理配置。</param>
    /// <param name="logger">日志。</param>
    public ServerClient(HttpClient http, IOptions<AgentOptions> options, ILogger<ServerClient> logger)
    {
        _http = http;
        _logger = logger;

        var serverUrl = (options.Value.ServerUrl ?? "").Trim().TrimEnd('/');
        if (serverUrl.Length == 0)
        {
            serverUrl = "http://localhost:5082";
        }

        // BaseAddress 必须以 '/' 结尾，才能与相对路径正确拼接
        _http.BaseAddress = new Uri(serverUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(15);

        ServerUrl = serverUrl;
        HostName = options.Value.EffectiveHostName;
        OsVersion = WindowsEnvironment.DescribeOs();
    }

    /// <summary>控制端地址。</summary>
    public string ServerUrl { get; }

    /// <summary>本机计算机名（注册时上报，也是控制台上的终端对象名称）。</summary>
    public string HostName { get; }

    /// <summary>本机操作系统版本描述。</summary>
    public string OsVersion { get; }

    /// <summary>向控制端注册终端。失败时返回 null（不抛异常）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<RegisterResponse?> RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new RegisterRequest
            {
                HostName = HostName,
                // IP 由控制端按 TCP 连接来源记录，比终端自报更可靠
                IpAddress = "",
                OsVersion = OsVersion,
                AgentVersion = Branding.EndpointProductVersion
            };

            using var response = await _http.PostAsJsonAsync("api/agent/register", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("终端注册失败，控制端返回状态码 {Code}", (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.Token))
            {
                _logger.LogWarning("终端注册响应内容不完整");
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "终端注册请求异常，控制端可能未启动");
            return null;
        }
    }

    /// <summary>发送心跳并取回该终端当前生效的策略。</summary>
    /// <param name="token">终端通信令牌。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<HeartbeatResult> HeartbeatAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/heartbeat");
            request.Headers.Add(TokenHeader, token ?? "");

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new HeartbeatResult { Unauthorized = true };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("心跳失败，控制端返回状态码 {Code}", (int)response.StatusCode);
                return new HeartbeatResult();
            }

            var policy = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken);
            return new HeartbeatResult { Success = true, Policy = policy };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "心跳请求异常，将在下个周期重试");
            return new HeartbeatResult();
        }
    }

    /// <summary>批量上报文件操作审计事件。</summary>
    /// <param name="token">终端通信令牌。</param>
    /// <param name="events">事件集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>上报成功返回 true；失败返回 false，调用方应把事件放回队列重试。</returns>
    public async Task<bool> ReportEventsAsync(
        string token, IReadOnlyList<AuditEventDto> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/events");
            request.Headers.Add(TokenHeader, token ?? "");
            request.Content = JsonContent.Create(events);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("审计事件上报失败，控制端返回状态码 {Code}", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审计事件上报请求异常，事件将保留在队列中重试");
            return false;
        }
    }
}
