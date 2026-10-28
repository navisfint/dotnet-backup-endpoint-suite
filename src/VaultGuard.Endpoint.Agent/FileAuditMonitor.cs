using Microsoft.Extensions.Logging;

namespace VaultGuard.Endpoint.Agent;

/// <summary>
/// 文件操作审计监控器。基于 <see cref="FileSystemWatcher"/> 监控策略指定的目录，
/// 把"新建 / 修改 / 删除 / 重命名"四类事件放入内存队列，由上报循环批量发送。
/// 设计要点：
/// <list type="bullet">
///   <item>加大 <c>InternalBufferSize</c> 降低高并发写入时丢事件的概率；</item>
///   <item>订阅 <c>Error</c> 事件，在缓冲区溢出等情况下自动重建监控器；</item>
///   <item>对短时间内的重复事件去重，避免同一次保存产生多条记录；</item>
///   <item>队列设置上限，超出时丢弃最旧事件，避免长时间离线导致内存无限增长。</item>
/// </list>
/// 本类所有异常都被内部消化，不会导致进程退出。
/// </summary>
public sealed class FileAuditMonitor : IDisposable
{
    /// <summary>队列长度上限。超出后丢弃最旧事件，防止离线期间内存失控。</summary>
    private const int MaxQueueLength = 20000;

    /// <summary>文件监控内部缓冲区大小（64KB），用于降低事件丢失概率。</summary>
    private const int WatcherBufferSize = 64 * 1024;

    /// <summary>去重时间窗口：同一路径同一类型在该窗口内的重复事件只记一条。</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    private readonly ILogger<FileAuditMonitor> _logger;

    /// <summary>保护队列、监控器列表与去重表的同步锁。</summary>
    private readonly object _sync = new();

    /// <summary>待上报事件队列。</summary>
    private readonly LinkedList<AuditEventDto> _queue = new();

    /// <summary>近期事件去重表：键为"类型|路径|目标路径"，值为最近一次记录时间。</summary>
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前已挂载的文件监控器。</summary>
    private readonly List<FileSystemWatcher> _watchers = new();

    /// <summary>策略要求的监控目录（原始期望值，重建监控时使用）。</summary>
    private IReadOnlyList<string> _desiredPaths = Array.Empty<string>();

    /// <summary>事件操作者用户名。进程运行期间不变，构造时取一次即可。</summary>
    private readonly string _userName;

    /// <summary>因队列溢出而被丢弃的事件累计数量。</summary>
    private long _droppedCount;

    /// <summary>构造文件审计监控器。</summary>
    /// <param name="logger">日志。</param>
    public FileAuditMonitor(ILogger<FileAuditMonitor> logger)
    {
        _logger = logger;
        _userName = WindowsEnvironment.CurrentUserName();
    }

    /// <summary>当前待上报的事件条数。</summary>
    public int PendingCount
    {
        get
        {
            lock (_sync)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>因队列溢出被丢弃的事件累计数量（供日志与排查用）。</summary>
    public long DroppedCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedCount;
            }
        }
    }

    /// <summary>
    /// 按最新策略更新被监控目录。目录集合未变化时不做任何动作；
    /// 变化时先释放旧的监控器再重新挂载。不存在的目录会被跳过并记录日志。
    /// </summary>
    /// <param name="paths">策略中的审计目录列表。</param>
    public void UpdatePaths(IReadOnlyList<string> paths)
    {
        var normalized = (paths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().TrimEnd('\\', '/'))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_sync)
        {
            if (SamePaths(_desiredPaths, normalized))
            {
                return;
            }

            _desiredPaths = normalized;
            RebuildWatchersLocked();
        }
    }

    /// <summary>
    /// 从队列头部取出最多 <paramref name="count"/> 条事件用于上报。
    /// </summary>
    /// <param name="count">本次最多取出的事件条数。</param>
    public List<AuditEventDto> TakeBatch(int count)
    {
        var batch = new List<AuditEventDto>();
        if (count <= 0)
        {
            return batch;
        }

        lock (_sync)
        {
            while (batch.Count < count && _queue.First is not null)
            {
                batch.Add(_queue.First.Value);
                _queue.RemoveFirst();
            }
        }

        return batch;
    }

    /// <summary>
    /// 上报失败时把事件放回队列头部，保证事件顺序不乱、下个周期重试。
    /// </summary>
    /// <param name="items">需要放回的事件集合。</param>
    public void ReturnBatch(IReadOnlyList<AuditEventDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                _queue.AddFirst(items[i]);
            }

            TrimLocked();
        }
    }

    /// <summary>
    /// 直接放入一条事件。用于管控策略执行失败等需要回报控制端的自定义事件。
    /// </summary>
    /// <param name="item">要上报的事件。</param>
    public void Enqueue(AuditEventDto item)
    {
        lock (_sync)
        {
            _queue.AddLast(item);
            TrimLocked();
        }
    }

    /// <summary>释放全部文件监控器。</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            DisposeWatchersLocked();
            _desiredPaths = Array.Empty<string>();
        }
    }

    // ============================================================
    //  内部实现
    // ============================================================

    /// <summary>判断两个目录集合是否一致（忽略顺序与大小写）。</summary>
    private static bool SamePaths(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var set = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
        return right.All(set.Contains);
    }

    /// <summary>释放全部监控器。调用方必须已持有 <see cref="_sync"/> 锁。</summary>
    private void DisposeWatchersLocked()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "释放文件监控器时发生异常（已忽略）");
            }
        }

        _watchers.Clear();
    }

    /// <summary>按当前期望目录重建全部监控器。调用方必须已持有 <see cref="_sync"/> 锁。</summary>
    private void RebuildWatchersLocked()
    {
        DisposeWatchersLocked();

        foreach (var directory in _desiredPaths)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("审计目录不存在，已跳过：{Directory}", directory);
                continue;
            }

            AttachWatcherLocked(directory);
        }
    }

    /// <summary>为单个目录挂载监控器。调用方必须已持有 <see cref="_sync"/> 锁。</summary>
    private void AttachWatcherLocked(string directory)
    {
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size
                               | NotifyFilters.CreationTime,
                InternalBufferSize = WatcherBufferSize
            };

            watcher.Created += (_, e) => RecordEvent(e.FullPath, AuditEventTypes.Created, "");
            watcher.Changed += (_, e) => RecordEvent(e.FullPath, AuditEventTypes.Modified, "");
            watcher.Deleted += (_, e) => RecordEvent(e.FullPath, AuditEventTypes.Deleted, "");

            // 重命名事件：path 记重命名前的路径，target_path 记重命名后的路径
            watcher.Renamed += (_, e) => RecordEvent(e.OldFullPath, AuditEventTypes.Renamed, e.FullPath);

            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            _logger.LogInformation("已开始监控目录：{Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "挂载目录监控失败，已跳过：{Directory}", directory);
        }
    }

    /// <summary>
    /// 文件监控出错（最常见的是内部缓冲区溢出）时，重建全部监控器，
    /// 使后续事件能够继续被捕获。
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "文件监控发生错误，正在重建全部监控器");

        lock (_sync)
        {
            try
            {
                RebuildWatchersLocked();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重建文件监控器失败（已忽略）");
            }
        }
    }

    /// <summary>把一条文件系统事件去重后放入待上报队列。</summary>
    /// <param name="path">发生变化的路径。</param>
    /// <param name="eventType">事件类型。</param>
    /// <param name="targetPath">重命名事件的目标路径，其余为空。</param>
    private void RecordEvent(string path, string eventType, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var key = eventType + "|" + path + "|" + targetPath;

        lock (_sync)
        {
            var now = DateTime.Now;

            // 同一次文件保存往往会连续触发多条 Changed，这里按时间窗口去重
            if (_recent.TryGetValue(key, out var last) && now - last < DuplicateWindow)
            {
                return;
            }

            _recent[key] = now;
            if (_recent.Count > 4096)
            {
                PruneRecentLocked(now);
            }

            _queue.AddLast(new AuditEventDto
            {
                EventType = eventType,
                Path = path,
                TargetPath = targetPath,
                UserName = _userName,
                OccurredAt = now
            });

            TrimLocked();
        }
    }

    /// <summary>清理去重表中已经过期的时间窗口记录。调用方必须已持有 <see cref="_sync"/> 锁。</summary>
    private void PruneRecentLocked(DateTime now)
    {
        var expired = _recent
            .Where(pair => now - pair.Value >= DuplicateWindow)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expired)
        {
            _recent.Remove(key);
        }
    }

    /// <summary>把队列裁剪到上限之内，超出的最旧事件被丢弃。调用方必须已持有 <see cref="_sync"/> 锁。</summary>
    private void TrimLocked()
    {
        var dropped = 0;
        while (_queue.Count > MaxQueueLength)
        {
            _queue.RemoveFirst();
            dropped++;
        }

        if (dropped > 0)
        {
            _droppedCount += dropped;
            _logger.LogWarning("待上报事件队列已满，丢弃最旧的 {Count} 条事件（累计丢弃 {Total} 条）",
                dropped, _droppedCount);
        }
    }
}
