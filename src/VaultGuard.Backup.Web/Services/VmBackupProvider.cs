namespace VaultGuard.Backup.Web.Services;

/// <summary>虚拟机无代理备份操作的返回结果。</summary>
/// <param name="Success">操作是否成功执行。</param>
/// <param name="Message">结果说明；未配置环境时承载明确的不适用提示。</param>
public readonly record struct VmBackupResult(bool Success, string Message);

/// <summary>
/// 虚拟机（vSphere / ESXi）无代理备份能力接口。
/// <para>
/// 按设计约定，本项为“有条件实现”：仅在目标环境具备**具备有效许可**的
/// vSphere/vCenter 时方可落地。当前交付版本仅保留本接口与对接说明文档，
/// 未提供真实实现，运行时由 <see cref="VmBackupProviderNotConfigured"/> 返回不适用提示。
/// </para>
/// </summary>
public interface IVmBackupProvider
{
    /// <summary>提供方名称。</summary>
    string ProviderName { get; }

    /// <summary>当前环境是否已配置并可用的虚拟机备份后端。</summary>
    bool IsConfigured { get; }

    /// <summary>不可用时的原因说明（供界面直接展示）。</summary>
    string UnavailableReason { get; }

    /// <summary>列举可备份的虚拟机名称集合。</summary>
    IReadOnlyList<string> ListVirtualMachines();

    /// <summary>为指定虚拟机创建快照备份并落盘到仓库目录。</summary>
    /// <param name="vmName">虚拟机名称。</param>
    /// <param name="repoPath">备份仓库根目录。</param>
    VmBackupResult CreateSnapshotBackup(string vmName, string repoPath);

    /// <summary>从指定快照/备份点还原虚拟机。</summary>
    /// <param name="vmName">虚拟机名称。</param>
    /// <param name="pointRef">备份点引用（快照标识或备份点编号）。</param>
    /// <param name="target">还原目标（数据存储/主机名），空表示原位置。</param>
    VmBackupResult RestoreSnapshot(string vmName, string pointRef, string target);
}

/// <summary>
/// 未配置虚拟机备份后端的占位实现。
/// <para>
/// 所有操作都会返回统一的“不适用”提示，绝不伪造成功结果：
/// 交付环境未检测到具备有效许可的 vSphere/vCenter，本项按设计约定不在本次范围。
/// </para>
/// </summary>
public sealed class VmBackupProviderNotConfigured : IVmBackupProvider
{
    /// <summary>统一的不适用提示语，界面与接口共用同一文案，避免各处写法不一致。</summary>
    public const string ReasonText =
        "未检测到具备有效许可的 vSphere/vCenter 环境，本项按设计约定不在本次范围。";

    /// <inheritdoc />
    public string ProviderName => "VMware 无代理备份（接口预留）";

    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public string UnavailableReason => ReasonText;

    /// <inheritdoc />
    public IReadOnlyList<string> ListVirtualMachines() => Array.Empty<string>();

    /// <inheritdoc />
    public VmBackupResult CreateSnapshotBackup(string vmName, string repoPath)
        => new(false, ReasonText);

    /// <inheritdoc />
    public VmBackupResult RestoreSnapshot(string vmName, string pointRef, string target)
        => new(false, ReasonText);
}
