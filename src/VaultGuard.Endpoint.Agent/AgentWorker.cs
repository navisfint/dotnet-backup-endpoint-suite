using VaultGuard.Common;

namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// 终端代理主工作循环。职责：
/// <list type="number">
///   <item>启动时向控制端注册并持久化终端令牌；</item>
///   <item>每 30 秒发送一次心跳，取回并执行最新管控策略；</item>
///   <item>每 10 秒或攒够 50 条时，批量上报文件操作审计事件。</item>
/// </list>
/// 设计要求：程序绝不因为任何异常退出——控制端不可达、注册表无权限、
/// 目录不存在等情形都只记录日志并继续运行。
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    /// <summary>心跳间隔。</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>上报循环的轮询间隔（用于判断"攒够 50 条"是否已满足）。</summary>
    private static readonly TimeSpan ReportPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>距离上次上报超过该时间即强制上报（对应"每 10 秒上报一次"）。</summary>
    private static readonly TimeSpan ReportMaxDelay = TimeSpan.FromSeconds(10);

    /// <summary>单批上报的事件条数上限。</summary>
    private const int ReportBatchSize = 50;

    /// <summary>单次上报动作最多连续发送的批次数，避免离线恢复后一次性压垮控制端。</summary>
    private const int MaxBatchesPerRound = 5;

    private readonly ILogger<AgentWorker> _logger;
    private readonly ServerClient _client;
    private readonly AgentStateStore _state;
    private readonly PolicyEnforcer _enforcer;
    private readonly FileAuditMonitor _monitor;

    /// <summary>已成功执行的"禁用控制面板"策略值，null 表示尚未执行过。</summary>
    private bool? _appliedNoControlPanel;

    /// <summary>已成功执行的"禁用 USB 存储"策略值，null 表示尚未执行过。</summary>
    private bool? _appliedDisableUsb;

    /// <summary>已生效的审计目录（分号拼接快照），用于判断是否需要重挂监控。</summary>
    private string _appliedAuditPaths = "";

    /// <summary>构造主工作循环。</summary>
    public AgentWorker(
        ILogger<AgentWorker> logger,
        ServerClient client,
        AgentStateStore state,
        PolicyEnforcer enforcer,
        FileAuditMonitor monitor)
    {
        _logger = logger;
        _client = client;
        _state = state;
        _enforcer = enforcer;
        _monitor = monitor;
    }

    /// <summary>程序主体：注册 → 心跳循环 + 上报循环（两者并行）。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Product} {Version} 终端代理启动。计算机名：{Host}，控制端：{Server}",
            Branding.EndpointProductName, Branding.EndpointProductVersion, _client.HostName, _client.ServerUrl);

        await EnsureRegisteredAsync(stoppingToken);

        // 心跳与事件上报互不阻塞：任一环节异常都不影响另一环节
        await Task.WhenAll(
            HeartbeatLoopAsync(stoppingToken),
            ReportLoopAsync(stoppingToken));

        _logger.LogInformation("终端代理已停止");
    }

    /// <summary>
    /// 确保终端已完成注册。已有有效身份时直接复用；
    /// 注册失败（例如控制端未启动）不阻塞流程，后续心跳会继续尝试。
    /// </summary>
    private async Task EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        if (_state.Identity.IsValid)
        {
            _logger.LogInformation("复用已保存的终端身份，终端编号 {AgentId}", _state.Identity.AgentId);
            return;
        }

        var response = await _client.RegisterAsync(cancellationToken);
        if (response is null)
        {
            _logger.LogWarning("终端注册未成功，稍后将继续尝试");
            return;
        }

        _state.Identity.AgentId = response.AgentId;
        _state.Identity.Token = response.Token;
        _state.Save();

        _logger.LogInformation("终端注册成功，终端编号 {AgentId}，允许接入：{Allowed}",
            response.AgentId, response.Allowed);
    }

    /// <summary>心跳循环：拉取策略并执行，令牌失效时自动重新注册。</summary>
    private async Task HeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_state.Identity.IsValid)
                {
                    // 尚未注册成功（控制端未启动等），本周期只做注册尝试
                    await EnsureRegisteredAsync(stoppingToken);
                }
                else
                {
                    var result = await _client.HeartbeatAsync(_state.Identity.Token, stoppingToken);

                    if (result.Unauthorized)
                    {
                        // 控制端不认这个令牌（例如控制端数据库被重建），清除本地身份后重新注册
                        _logger.LogWarning("终端令牌已被控制端拒绝，正在重新注册");
                        _state.Reset();
                        await EnsureRegisteredAsync(stoppingToken);
                    }
                    else if (result.Success && result.Policy is not null)
                    {
                        ApplyPolicy(result.Policy);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 心跳环节的任何异常都不允许影响进程存活
                _logger.LogError(ex, "心跳循环发生异常（已忽略，继续运行）");
            }

            await DelaySafeAsync(HeartbeatInterval, stoppingToken);
        }
    }

    /// <summary>上报循环：每 10 秒或攒够 50 条时把队列中的事件批量发送给控制端。</summary>
    private async Task ReportLoopAsync(CancellationToken stoppingToken)
    {
        var lastFlush = DateTime.Now;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = _monitor.PendingCount;
                var batchFull = pending >= ReportBatchSize;
                var timeUp = DateTime.Now - lastFlush >= ReportMaxDelay;

                if (pending > 0 && _state.Identity.IsValid && (batchFull || timeUp))
                {
                    lastFlush = DateTime.Now;

                    for (var round = 0; round < MaxBatchesPerRound; round++)
                    {
                        if (_monitor.PendingCount <= 0)
                        {
                            break;
                        }

                        var batch = _monitor.TakeBatch(ReportBatchSize);
                        if (batch.Count == 0)
                        {
                            break;
                        }

                        if (await _client.ReportEventsAsync(_state.Identity.Token, batch, stoppingToken))
                        {
                            _logger.LogInformation("已上报 {Count} 条审计事件", batch.Count);
                        }
                        else
                        {
                            // 上报失败则放回队列，下个周期重试，避免事件丢失
                            _monitor.ReturnBatch(batch);
                            _logger.LogWarning("审计事件上报失败，{Count} 条已放回队列等待重试", batch.Count);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件上报循环发生异常（已忽略，继续运行）");
            }

            await DelaySafeAsync(ReportPollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// 执行控制端下发的策略。只有当策略值与上次成功执行的值不同时才真正写注册表，
    /// 避免每 30 秒重复写入；执行失败时把失败原因作为告警事件回报控制端，
    /// 并在下个心跳周期重试。
    /// </summary>
    /// <param name="policy">控制端返回的策略。</param>
    private void ApplyPolicy(HeartbeatResponse policy)
    {
        // ---- 电脑使用权限：禁用控制面板 ----
        if (_appliedNoControlPanel != policy.NoControlPanel)
        {
            var error = _enforcer.ApplyControlPanel(policy.NoControlPanel);
            if (error is null)
            {
                _appliedNoControlPanel = policy.NoControlPanel;
                _logger.LogInformation("控制面板策略已生效：{State}", policy.NoControlPanel ? "禁用" : "恢复");
            }
            else
            {
                ReportPolicyAlert(error);
            }
        }

        // ---- 电脑使用权限：禁用 USB 存储设备 ----
        if (_appliedDisableUsb != policy.DisableUsb)
        {
            var error = _enforcer.ApplyUsbStorage(policy.DisableUsb);
            if (error is null)
            {
                _appliedDisableUsb = policy.DisableUsb;
                _logger.LogInformation("USB 存储策略已生效：{State}", policy.DisableUsb ? "禁用" : "恢复");
            }
            else
            {
                ReportPolicyAlert(error);
            }
        }

        // ---- 文件操作审计目录 ----
        var paths = policy.AuditPaths ?? new List<string>();
        var snapshot = string.Join(";", paths);
        if (!string.Equals(_appliedAuditPaths, snapshot, StringComparison.OrdinalIgnoreCase))
        {
            _monitor.UpdatePaths(paths);
            _appliedAuditPaths = snapshot;
            _logger.LogInformation("审计目录已更新，共 {Count} 个目录", paths.Count);
        }
    }

    /// <summary>把管控策略执行失败的原因作为告警事件回报控制端。</summary>
    /// <param name="reason">失败原因（可直接展示在控制台的审计日志中）。</param>
    private void ReportPolicyAlert(string reason)
    {
        _monitor.Enqueue(new AuditEventDto
        {
            EventType = AuditEventTypes.PolicyAlert,
            Path = reason,
            TargetPath = "",
            UserName = WindowsEnvironment.CurrentUserName(),
            OccurredAt = DateTime.Now
        });

        _logger.LogWarning("策略执行失败，已回报控制端：{Reason}", reason);
    }

    /// <summary>可取消的延时。取消时静默返回，避免退出流程被异常打断。</summary>
    private static async Task DelaySafeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常停机路径，无需处理
        }
    }
}
