using System.Text.Json;
using VaultGuard.Common.Util;

namespace VaultGuard.Endpoint.Agent;

/// <summary>终端在控制端的身份信息（注册结果）。</summary>
public sealed class AgentIdentity
{
    /// <summary>控制端分配的终端编号。</summary>
    public long AgentId { get; set; }

    /// <summary>与控制端通信用的令牌。</summary>
    public string Token { get; set; } = "";

    /// <summary>身份是否有效（已注册并取得令牌）。</summary>
    public bool IsValid => AgentId > 0 && !string.IsNullOrWhiteSpace(Token);
}

/// <summary>
/// 终端身份持久化。注册结果写入程序目录 <c>data/agent.json</c>，
/// 程序重启后直接复用，避免每次启动都重新注册。
/// </summary>
public sealed class AgentStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<AgentStateStore> _logger;
    private readonly string _filePath = Path.Combine(AppPath.DataDir, "agent.json");

    /// <summary>构造终端身份存储，并立即尝试从磁盘载入已有身份。</summary>
    public AgentStateStore(ILogger<AgentStateStore> logger)
    {
        _logger = logger;
        Load();
    }

    /// <summary>当前终端身份。</summary>
    public AgentIdentity Identity { get; private set; } = new();

    /// <summary>从磁盘载入身份信息；文件不存在或内容损坏时视为未注册。</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            var identity = JsonSerializer.Deserialize<AgentIdentity>(json);
            if (identity is not null)
            {
                Identity = identity;
            }
        }
        catch (Exception ex)
        {
            // 身份文件损坏不影响启动，重新注册即可
            _logger.LogWarning(ex, "读取终端身份文件失败，将按未注册处理");
            Identity = new AgentIdentity();
        }
    }

    /// <summary>把当前身份保存到磁盘。</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Identity, JsonOptions));
        }
        catch (Exception ex)
        {
            // 保存失败只影响下次启动是否复用身份，不阻断当前运行
            _logger.LogWarning(ex, "写入终端身份文件失败");
        }
    }

    /// <summary>清除本地身份信息（令牌被控制端拒绝时使用）。</summary>
    public void Reset()
    {
        Identity = new AgentIdentity();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除终端身份文件失败");
        }
    }
}
