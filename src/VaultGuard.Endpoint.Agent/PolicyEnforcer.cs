using Microsoft.Win32;

namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// 终端管控策略执行器。把控制端下发的策略落到本机注册表，
/// 实现"禁用控制面板""禁用 USB 存储设备"两项电脑使用权限管控。
/// 所有方法都不抛异常：失败时返回原因文本，由调用方回报控制端。
/// </summary>
public sealed class PolicyEnforcer
{
    /// <summary>控制面板限制策略项的相对路径（HKLM 与 HKCU 下同名，写入时两处都要落）。</summary>
    private const string ExplorerPolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    /// <summary>禁用控制面板的值名称。</summary>
    private const string NoControlPanelValue = "NoControlPanel";

    /// <summary>USB 大容量存储驱动服务项。</summary>
    private const string UsbStorServicePath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";

    /// <summary>USBSTOR 服务的启动类型值名称。</summary>
    private const string UsbStorStartValue = "Start";

    /// <summary>服务启动类型：禁用。</summary>
    private const int UsbStorDisabled = 4;

    /// <summary>服务启动类型：手动（系统默认值）。</summary>
    private const int UsbStorManual = 3;

    private readonly ILogger<PolicyEnforcer> _logger;

    /// <summary>构造策略执行器。</summary>
    /// <param name="logger">日志。</param>
    public PolicyEnforcer(ILogger<PolicyEnforcer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用"禁用控制面板"策略。
    /// <para>
    /// 控制面板限制有两处落点，生效范围不同，本方法**两处都写**：
    /// </para>
    /// <list type="bullet">
    /// <item><c>HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoControlPanel</c>
    /// —— 机器级，对本机**全部用户**生效，需要管理员权限。终端代理以 Windows 服务（SYSTEM）方式运行时，
    /// 这是真正生效的落点。</item>
    /// <item><c>HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoControlPanel</c>
    /// —— 用户级，只对该注册表配置单元所属的用户生效。以控制台方式、由当前登录用户启动代理时有效；
    /// 若代理以服务方式运行，写的是服务账号的配置单元，对登录用户没有影响，因此仅作为补充手段。</item>
    /// </list>
    /// 只要两处中有一处写入成功即视为策略已下发；两处都失败才回报失败原因。
    /// </summary>
    /// <param name="disable">true 表示禁用控制面板；false 表示恢复。</param>
    /// <returns>成功返回 null；失败返回可直接展示给管理员的失败原因。</returns>
    public string? ApplyControlPanel(bool disable)
    {
        // 机器级：服务形态下的主落点，对全部用户生效
        var machineError = ApplyExplorerPolicy(Registry.LocalMachine, "机器级", disable);

        // 用户级：控制台形态下对当前用户生效；失败不影响整体结论
        var userError = ApplyExplorerPolicy(Registry.CurrentUser, "用户级", disable);

        if (machineError is null || userError is null)
        {
            _logger.LogInformation("控制面板策略已下发（disable={Disable}）", disable);
            return null;
        }

        return $"禁用控制面板失败：{machineError}";
    }

    /// <summary>
    /// 在指定的注册表根（HKLM 或 HKCU）下写入或删除 <c>NoControlPanel</c> 策略值。
    /// </summary>
    /// <param name="root">注册表根：<see cref="Registry.LocalMachine"/> 或 <see cref="Registry.CurrentUser"/>。</param>
    /// <param name="scopeName">范围名称，仅用于日志与错误提示。</param>
    /// <param name="disable">true 写入 1（禁用）；false 删除该值（恢复）。</param>
    /// <returns>成功返回 null；失败返回失败原因。</returns>
    private string? ApplyExplorerPolicy(RegistryKey root, string scopeName, bool disable)
    {
        try
        {
            using var key = root.CreateSubKey(ExplorerPolicyPath, writable: true);
            if (key is null)
            {
                return $"{scopeName}策略项无法打开，可能被安全软件拦截";
            }

            if (disable)
            {
                key.SetValue(NoControlPanelValue, 1, RegistryValueKind.DWord);
            }
            else if (key.GetValue(NoControlPanelValue) is not null)
            {
                // 值不存在时 DeleteValue 不报错，这里先判断再删除以便日志准确
                key.DeleteValue(NoControlPanelValue, throwOnMissingValue: false);
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return $"{scopeName}策略项写入权限不足，请以管理员身份运行终端代理";
        }
        catch (Exception ex)
        {
            return $"{scopeName}策略项写入失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 应用"禁用 USB 存储设备"策略。
    /// 禁用时把 <c>HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR\Start</c> 置为 4（禁用），
    /// 取消时置回 3（手动，即系统默认）。该键位于 HKLM，需要管理员权限，
    /// 权限不足时返回失败原因并保持程序继续运行。
    /// </summary>
    /// <param name="disable">true 表示禁用 USB 存储设备；false 表示恢复。</param>
    /// <returns>成功返回 null；失败返回可直接展示给管理员的失败原因。</returns>
    public string? ApplyUsbStorage(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UsbStorServicePath, writable: true);
            if (key is null)
            {
                return "禁用 USB 存储失败：无法打开 HKLM\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR，请以管理员权限运行终端代理";
            }

            key.SetValue(UsbStorStartValue, disable ? UsbStorDisabled : UsbStorManual, RegistryValueKind.DWord);

            if (disable)
            {
                _logger.LogInformation("已写入 USBSTOR Start=4，USB 存储设备已禁用");
            }
            else
            {
                _logger.LogInformation("已写入 USBSTOR Start=3，USB 存储设备已恢复");
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // 这是最常见的情况：服务未以管理员身份运行
            _logger.LogWarning("应用 USB 存储策略失败：权限不足");
            return "禁用 USB 存储失败：权限不足，请以管理员身份运行终端代理";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "应用 USB 存储策略失败");
            return $"禁用 USB 存储失败：{ex.Message}";
        }
    }
}
