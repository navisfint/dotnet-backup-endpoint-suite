using Microsoft.Extensions.Logging;

namespace VaultGuard.Endpoint.Agent.Logging;

/// <summary>
/// 简易文件日志提供器。把运行日志同时写入程序目录下的 <c>data/logs</c>，
/// 便于在没有控制台窗口的 Windows 服务形态下排查问题。
/// 写文件失败时静默忽略，绝不影响主流程。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();

    /// <summary>构造文件日志提供器。</summary>
    /// <param name="directory">日志目录（通常传入 AppPath.LogDir）。</param>
    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch
        {
            // 目录建不出来时降级为"只写控制台"，不影响代理运行
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>释放资源（本实现无需释放，保留以满足接口约定）。</summary>
    public void Dispose()
    {
        // 每次写入都是追加并立即关闭文件句柄，无长期持有资源
    }

    /// <summary>把一行日志追加写入当天的日志文件。</summary>
    internal void WriteLine(string line)
    {
        try
        {
            var file = Path.Combine(_directory, $"agent-{DateTime.Now:yyyyMMdd}.log");
            lock (_gate)
            {
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // 磁盘写满、权限不足等情况直接忽略，避免日志组件把代理拖垮
        }
    }

    /// <summary>单个类别的文件日志实现。</summary>
    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        /// <summary>只记录"信息"及以上级别，避免调试日志把文件写爆。</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <summary>本实现不处理日志作用域，直接返回空作用域。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>写入一条日志。</summary>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{LevelText(logLevel)}] {_category} - {message}";

            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.WriteLine(line);
        }

        /// <summary>把日志级别映射为简短的中文/英文标记。</summary>
        private static string LevelText(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => "NONE"
        };
    }
}
