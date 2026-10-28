using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZstdSharp;
using VaultGuard.Common.Models;
using VaultGuard.Common.Util;

namespace VaultGuard.Backup.Web.Services;

/// <summary>备份仓库中的快照清单（manifest）文档结构。清单本身即“该时间点的完整逻辑视图”。</summary>
public sealed class BackupManifest
{
    /// <summary>清单对应的备份点编号。</summary>
    public long PointId { get; set; }

    /// <summary>所属任务编号。</summary>
    public long JobId { get; set; }

    /// <summary>任务名称快照。</summary>
    public string JobName { get; set; } = "";

    /// <summary>主机名称快照。</summary>
    public string HostName { get; set; } = "";

    /// <summary>备份模式（0 全量 / 1 差异 / 2 增量）。</summary>
    public int Mode { get; set; }

    /// <summary>备份开始时间。</summary>
    public string StartedAt { get; set; } = "";

    /// <summary>备份结束时间。</summary>
    public string FinishedAt { get; set; } = "";

    /// <summary>该时间点的全部逻辑文件条目（含沿用历史副本的条目）。</summary>
    public List<FileEntry> Entries { get; set; } = new();
}

/// <summary>一次备份执行的结果摘要。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="PointId">备份点编号。</param>
/// <param name="CopiedCount">本次实际复制的文件数。</param>
/// <param name="CopiedBytes">本次实际复制的字节数。</param>
/// <param name="TotalCount">逻辑文件总数。</param>
/// <param name="TotalBytes">逻辑总字节数。</param>
/// <param name="ElapsedSeconds">耗时（秒）。</param>
/// <param name="Message">结果说明或失败原因。</param>
public readonly record struct BackupRunResult(
    bool Success, long PointId, int CopiedCount, long CopiedBytes,
    int TotalCount, long TotalBytes, double ElapsedSeconds, string Message);

/// <summary>一次文件还原执行的结果摘要。</summary>
/// <param name="Success">是否成功（存在任一文件失败即为 false）。</param>
/// <param name="RestoredCount">成功还原的文件数。</param>
/// <param name="FailedCount">还原失败的文件数。</param>
/// <param name="TotalBytes">成功还原的逻辑字节数。</param>
/// <param name="Message">结果说明。</param>
public readonly record struct RestoreRunResult(
    bool Success, int RestoredCount, int FailedCount, long TotalBytes, string Message);

/// <summary>
/// 备份与还原引擎。按“仓库目录 + 快照清单比对”的方式实现全量 / 差异 / 增量三种模式：
/// <list type="number">
///   <item>写入 backup_points 记录（status=进行中）；</item>
///   <item>展开来源目录、按排除规则过滤并递归扫描得到文件清单（不含 CRC）；</item>
///   <item>按模式选取基线清单，逐文件比对大小与最后写入时间，必要时以 CRC32 复核内容；</item>
///   <item>需复制的文件以 Zstd 压缩落盘，未变的文件沿用历史副本；</item>
///   <item>写出该时间点的完整逻辑清单并回填统计。</item>
/// </list>
/// 引擎内部对单次任务整体做了异常隔离：任何异常都只把该备份点标记为失败并返回，
/// 绝不向外抛出把 Web 请求打崩。
/// </summary>
public sealed class BackupEngine
{
    private readonly BackupRepository _repo;
    private readonly CredentialStore _credentials;
    private readonly ILogger<BackupEngine> _logger;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        // 中文路径不做 Unicode 转义，清单文件保持可直接阅读
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>构造备份引擎。</summary>
    public BackupEngine(BackupRepository repo, CredentialStore credentials, ILogger<BackupEngine> logger)
    {
        _repo = repo;
        _credentials = credentials;
        _logger = logger;
    }

    // ============================================================
    //  备份
    // ============================================================

    /// <summary>
    /// 执行一次备份。同步执行，返回结果摘要；失败不会抛出异常。
    /// </summary>
    /// <param name="jobId">备份任务编号。</param>
    /// <param name="actor">操作人（用于日志）。</param>
    public BackupRunResult Run(long jobId, string actor)
    {
        var job = _repo.GetJob(jobId);
        if (job is null)
        {
            return new BackupRunResult(false, 0, 0, 0, 0, 0, 0, "备份任务不存在");
        }

        var mode = (BackupMode)Row.Int(job, "mode");
        var jobName = Row.Str(job, "name");
        var hostName = Row.Str(job, "host_name");
        var sourcesText = Row.Str(job, "sources");
        var repoRoot = Row.Str(job, "repo_path").Trim();
        var excludes = Display.SplitList(Row.Str(job, "excludes"));

        var startedAt = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        // 先落一条“进行中”记录，便于失败时也能在备份点列表看到本次执行
        var pointId = _repo.InsertRunningPoint(jobId, jobName, hostName, mode, sourcesText, startedAt);

        try
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new InvalidOperationException("备份仓库目录未填写，请在任务中配置仓库路径。");
            }

            var sourceRoots = ResolveSourceRoots(sourcesText);
            if (sourceRoots.Count == 0)
            {
                throw new InvalidOperationException("数据来源为空，请填写本地目录、盘符或 UNC 共享路径。");
            }

            var jobDir = Path.Combine(repoRoot, $"job-{jobId}");
            var timestamp = startedAt.ToString("yyyyMMddHHmmss");
            var dataDir = Path.Combine(jobDir, "data", timestamp);
            var manifestPath = Path.Combine(jobDir, $"manifest-{pointId}.json");

            Directory.CreateDirectory(jobDir);

            // 远程 UNC 来源：以主机对象保存的账号口令建立网络会话，用完即断
            using var session = EstablishNetworkSessionIfNeeded(job, sourcesText);

            var warnings = new List<string>();
            var scanned = ScanFiles(sourceRoots, excludes, warnings);

            // 选取基线清单：全量无基线；差异取最近一次成功全量；增量取最近一次成功备份
            var baseline = LoadBaseline(jobId, mode, warnings);

            var entries = new List<FileEntry>(scanned.Count);
            var copiedCount = 0;
            long copiedBytes = 0;
            long totalBytes = 0;

            foreach (var file in scanned)
            {
                var entry = new FileEntry
                {
                    RelPath = file.RelPath,
                    Length = file.Length,
                    LastWriteUtcTicks = file.LastWriteUtcTicks
                };

                baseline.TryGetValue(file.RelPath, out var baseEntry);

                var needCopy = true;
                uint? knownCrc = null;

                if (baseEntry is not null &&
                    baseEntry.Length == file.Length &&
                    baseEntry.LastWriteUtcTicks == file.LastWriteUtcTicks)
                {
                    // 大小与最后写入时间都一致：直接沿用历史副本，不读内容
                    needCopy = false;
                }
                else if (baseEntry is not null)
                {
                    // 时间戳或长度变化：用 CRC32 复核内容是否真的变了
                    knownCrc = FileHash.ComputeFileCrc32(file.AbsolutePath);
                    if (knownCrc.Value == baseEntry.Crc32)
                    {
                        needCopy = false;
                    }
                }

                if (needCopy)
                {
                    entry.RepoRelPath = CopyToRepo(file.AbsolutePath, file.RelPath, dataDir, timestamp);
                    entry.SourcePointId = pointId;
                    entry.Crc32 = knownCrc ?? FileHash.ComputeFileCrc32(file.AbsolutePath);
                    copiedCount++;
                    copiedBytes += file.Length;
                }
                else
                {
                    // 沿用基线中的历史副本，指向其来源备份点
                    entry.RepoRelPath = baseEntry!.RepoRelPath;
                    entry.SourcePointId = baseEntry.SourcePointId;
                    entry.Crc32 = baseEntry.Crc32;
                }

                totalBytes += file.Length;
                entries.Add(entry);
            }

            // 写出该时间点的完整逻辑清单（包含未复制但沿用历史副本的条目）
            var manifest = new BackupManifest
            {
                PointId = pointId,
                JobId = jobId,
                JobName = jobName,
                HostName = hostName,
                Mode = (int)mode,
                StartedAt = Row.ToDbText(startedAt),
                FinishedAt = Row.ToDbText(DateTime.Now),
                Entries = entries
            };
            WriteManifest(manifestPath, manifest);

            stopwatch.Stop();
            _repo.CompletePoint(pointId, manifestPath, copiedCount, copiedBytes,
                entries.Count, totalBytes, DateTime.Now, stopwatch.Elapsed.TotalSeconds);

            var summary = $"备份完成：逻辑 {entries.Count} 个文件（{Display.Size(totalBytes)}），" +
                          $"本次实际复制 {copiedCount} 个（{Display.Size(copiedBytes)}）。";
            if (warnings.Count > 0)
            {
                summary += " 部分目录无访问权限已跳过：" + string.Join("；", warnings.Take(5));
            }

            _repo.LogOperation(actor, "执行备份", jobName,
                $"{Display.ModeName(mode)}备份成功，备份点 #{pointId}，复制 {copiedCount} 个文件");
            _logger.LogInformation("备份任务 {Job}（#{Point}）成功：复制 {Copied} 个文件", jobName, pointId, copiedCount);

            return new BackupRunResult(true, pointId, copiedCount, copiedBytes,
                entries.Count, totalBytes, stopwatch.Elapsed.TotalSeconds, summary);
        }
        catch (Exception ex)
        {
            // 单次备份失败只影响本条记录，绝不影响 Web 进程
            stopwatch.Stop();
            _repo.FailPoint(pointId, ex.Message, DateTime.Now, stopwatch.Elapsed.TotalSeconds);
            _repo.LogOperation(actor, "执行备份", jobName, $"备份失败：{ex.Message}");
            _logger.LogError(ex, "备份任务 {Job} 执行失败", jobName);

            return new BackupRunResult(false, pointId, 0, 0, 0, 0,
                stopwatch.Elapsed.TotalSeconds, "备份失败：" + ex.Message);
        }
    }

    // ============================================================
    //  还原
    // ============================================================

    /// <summary>
    /// 从指定备份点还原选中的文件。目标目录为空时还原到各文件在来源主机上的原始绝对位置。
    /// </summary>
    /// <param name="pointId">备份点编号。</param>
    /// <param name="relPaths">选中的文件相对路径集合。</param>
    /// <param name="targetDir">还原目标目录；为空表示还原到原位置。</param>
    /// <param name="actor">操作人。</param>
    public RestoreRunResult Restore(long pointId, IReadOnlyList<string> relPaths, string? targetDir, string actor)
    {
        var point = _repo.GetPoint(pointId);
        if (point is null)
        {
            return new RestoreRunResult(false, 0, 0, 0, "备份点不存在");
        }

        if (relPaths.Count == 0)
        {
            return new RestoreRunResult(false, 0, 0, 0, "请至少勾选一个待还原文件");
        }

        var jobId = Row.Long(point, "job_id");
        var jobName = Row.Str(point, "job_name");
        var manifestPath = Row.Str(point, "manifest_path");
        var target = (targetDir ?? "").Trim();

        try
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                throw new InvalidOperationException("备份点清单文件不存在或已被移动，无法还原。");
            }

            var manifest = LoadManifest(manifestPath);
            // 相对路径字典：Windows 下路径不区分大小写
            var index = manifest.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.RelPath))
                .GroupBy(e => e.RelPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var jobDir = Path.GetDirectoryName(manifestPath) ?? "";
            var prefixRoots = BuildPrefixRootMap(Row.Str(point, "sources"));

            var restored = 0;
            var failed = 0;
            long restoredBytes = 0;
            var errors = new List<string>();

            foreach (var rel in relPaths)
            {
                if (!index.TryGetValue(rel, out var entry) || string.IsNullOrEmpty(entry.RepoRelPath))
                {
                    failed++;
                    errors.Add($"{rel}：清单中不存在该条目");
                    continue;
                }

                var repoFile = Path.Combine(jobDir, entry.RepoRelPath);
                if (!File.Exists(repoFile))
                {
                    failed++;
                    errors.Add($"{rel}：仓库副本缺失");
                    continue;
                }

                // 目标目录为空 → 还原到原始绝对位置；否则落到目标目录下并保留相对结构
                string destPath;
                if (string.IsNullOrEmpty(target))
                {
                    if (!TryResolveOriginalPath(rel, prefixRoots, out destPath))
                    {
                        failed++;
                        errors.Add($"{rel}：无法解析原始位置，请指定目标目录");
                        continue;
                    }
                }
                else
                {
                    destPath = Path.Combine(target, rel);
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // .zst 结尾的先解压，其余直接复制；均覆盖同名文件
                WriteOut(repoFile, destPath);

                restored++;
                restoredBytes += entry.Length;
            }

            var status = failed == 0 ? "成功" : "部分失败";
            _repo.InsertRestore(new RestoreRecord
            {
                PointId = pointId,
                Files = string.Join(";", relPaths),
                Target = target,
                FileCount = restored,
                TotalBytes = restoredBytes,
                Operator = actor,
                Status = status,
                CreatedAt = DateTime.Now
            });

            _repo.LogOperation(actor, "还原文件", $"备份点 #{pointId}",
                $"{jobName}：成功还原 {restored} 个文件，失败 {failed} 个");

            var message = failed == 0
                ? $"还原完成：成功还原 {restored} 个文件（{Display.Size(restoredBytes)}）。"
                : $"还原部分失败：成功 {restored} 个，失败 {failed} 个。" + string.Join("；", errors.Take(5));

            return new RestoreRunResult(failed == 0, restored, failed, restoredBytes, message);
        }
        catch (Exception ex)
        {
            _repo.LogOperation(actor, "还原文件", $"备份点 #{pointId}", $"还原失败：{ex.Message}");
            _logger.LogError(ex, "备份点 #{Point} 还原失败", pointId);
            return new RestoreRunResult(false, 0, 0, 0, "还原失败：" + ex.Message);
        }
    }

    /// <summary>按选中条目对应的清单读取文件清单（供还原页展示）。</summary>
    /// <param name="pointId">备份点编号。</param>
    /// <returns>清单条目集合；清单缺失时返回空集合。</returns>
    public List<FileEntry> ReadManifestEntries(long pointId)
    {
        var point = _repo.GetPoint(pointId);
        if (point is null)
        {
            return new List<FileEntry>();
        }

        var manifestPath = Row.Str(point, "manifest_path");
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return new List<FileEntry>();
        }

        return LoadManifest(manifestPath).Entries;
    }

    // ============================================================
    //  内部实现
    // ============================================================

    /// <summary>一个扫描到的来源根目录及其在清单中使用的路径前缀。</summary>
    private sealed record SourceRoot(string Root, string Prefix);

    /// <summary>一个扫描到的文件。</summary>
    private sealed record ScannedFile(string AbsolutePath, string RelPath, long Length, long LastWriteUtcTicks);

    /// <summary>把来源文本解析为规范化根目录集合，并为每个根目录分配清单前缀。</summary>
    /// <param name="sourcesText">分号（或换行）分隔的来源列表。</param>
    /// <returns>来源根目录集合。</returns>
    /// <exception cref="InvalidOperationException">来源路径非法时抛出。</exception>
    private static List<SourceRoot> ResolveSourceRoots(string sourcesText)
    {
        var result = new List<SourceRoot>();
        var usedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in Display.SplitList(sourcesText))
        {
            var source = raw.Trim();

            // 形如 "D:" 的裸盘符补成 "D:\"，避免被解析为“D 盘当前目录”
            if (source.Length == 2 && char.IsLetter(source[0]) && source[1] == ':')
            {
                source += "\\";
            }

            string root;
            try
            {
                root = Path.GetFullPath(source);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"数据来源路径格式非法：{raw}");
            }

            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException($"数据来源目录不存在或不可访问：{root}");
            }

            var prefix = BuildPrefix(root, usedPrefixes);
            result.Add(new SourceRoot(root, prefix));
        }

        return result;
    }

    /// <summary>为来源根目录生成一个在清单内唯一的路径前缀。</summary>
    private static string BuildPrefix(string root, HashSet<string> used)
    {
        var trimmed = root.TrimEnd('\\', '/');
        var leaf = Path.GetFileName(trimmed);

        // 盘符根目录（如 D:\）取盘符本身作为前缀
        if (string.IsNullOrEmpty(leaf))
        {
            leaf = root.TrimEnd('\\', '/').TrimEnd(':');
            if (leaf.Length >= 2 && leaf[1] == ':')
            {
                leaf = leaf.Substring(0, 1);
            }
        }

        if (string.IsNullOrEmpty(leaf))
        {
            leaf = "root";
        }

        // 去掉文件名非法字符，避免前缀本身无法作为路径片段
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            leaf = leaf.Replace(c, '_');
        }

        var prefix = leaf;
        var suffix = 2;
        while (!used.Add(prefix))
        {
            prefix = leaf + "_" + suffix;
            suffix++;
        }

        return prefix;
    }

    /// <summary>
    /// 递归扫描来源目录，返回文件清单。为了对无访问权限的子目录具备容错能力，
    /// 采用手工遍历（逐个目录 try/catch），而不是一次性 AllDirectories 枚举。
    /// </summary>
    /// <param name="roots">来源根目录集合。</param>
    /// <param name="excludes">排除通配符集合。</param>
    /// <param name="warnings">累积的无权限目录提示。</param>
    private static List<ScannedFile> ScanFiles(
        IReadOnlyList<SourceRoot> roots, IReadOnlyList<string> excludes, List<string> warnings)
    {
        var files = new List<ScannedFile>();

        foreach (var root in roots)
        {
            var pending = new Stack<string>();
            pending.Push(root.Root);

            while (pending.Count > 0)
            {
                var dir = pending.Pop();

                string[] subDirs;
                string[] dirFiles;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                    dirFiles = Directory.GetFiles(dir);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{dir}（{ex.Message}）");
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    // 目录名命中排除规则时整棵子树跳过
                    var dirName = Path.GetFileName(sub);
                    if (excludes.Any(p => Display.WildcardMatch(p, dirName)))
                    {
                        continue;
                    }

                    pending.Push(sub);
                }

                foreach (var path in dirFiles)
                {
                    var rel = BuildRelPath(root, path);
                    var fileName = Path.GetFileName(path);

                    if (IsExcluded(rel, fileName, excludes))
                    {
                        continue;
                    }

                    long length;
                    long ticks;
                    try
                    {
                        var info = new FileInfo(path);
                        length = info.Length;
                        ticks = info.LastWriteTimeUtc.Ticks;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"{path}（{ex.Message}）");
                        continue;
                    }

                    files.Add(new ScannedFile(path, rel, length, ticks));
                }
            }
        }

        return files;
    }

    /// <summary>计算文件相对来源根目录的路径，并加上来源前缀，统一以 '\' 分隔。</summary>
    private static string BuildRelPath(SourceRoot root, string absolutePath)
    {
        var relative = absolutePath.Substring(root.Root.TrimEnd('\\', '/').Length)
            .TrimStart('\\', '/')
            .Replace('/', '\\');
        return root.Prefix.Length == 0 ? relative : root.Prefix + "\\" + relative;
    }

    /// <summary>判断文件是否命中排除规则（匹配文件名或相对路径）。</summary>
    private static bool IsExcluded(string relPath, string fileName, IReadOnlyList<string> excludes)
        => excludes.Any(p => Display.WildcardMatch(p, fileName) || Display.WildcardMatch(p, relPath));

    /// <summary>
    /// 选取本次备份的基线清单条目。全量无基线；差异取最近一次成功全量；增量取最近一次成功备份（任意模式）。
    /// </summary>
    private Dictionary<string, FileEntry> LoadBaseline(long jobId, BackupMode mode, List<string> warnings)
    {
        var result = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        if (mode == BackupMode.Full)
        {
            return result; // 全量：不比对基线
        }

        var baselineMode = mode == BackupMode.Differential ? BackupMode.Full : (BackupMode?)null;
        var baselinePoint = _repo.FindLatestSuccessPoint(jobId, baselineMode);
        if (baselinePoint is null)
        {
            return result; // 尚无可用基线，本次按全量处理
        }

        var manifestPath = Row.Str(baselinePoint, "manifest_path");
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            warnings.Add($"基线备份点 #{Row.Long(baselinePoint, "id")} 的清单缺失，本次按全量处理");
            return result;
        }

        foreach (var entry in LoadManifest(manifestPath).Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.RelPath))
            {
                result[entry.RelPath] = entry;
            }
        }

        return result;
    }

    /// <summary>把来源文件以 Zstd（级别 3）压缩写入本次数据目录，返回相对 job 目录的仓库路径。</summary>
    private static string CopyToRepo(string sourcePath, string relPath, string dataDir, string timestamp)
    {
        var destPath = Path.Combine(dataDir, relPath + ".zst");
        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var dest = File.Create(destPath))
        using (var zstd = new CompressionStream(dest, 3))
        {
            source.CopyTo(zstd);
        }

        // 仓库相对路径统一使用 '\' 分隔，形如 data\{时间戳}\{相对路径}.zst
        return "data\\" + timestamp + "\\" + relPath + ".zst";
    }

    /// <summary>写出文件到还原目标：.zst 结尾的先解压，否则直接复制。</summary>
    private static void WriteOut(string repoFile, string destPath)
    {
        if (repoFile.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
        {
            using var source = new FileStream(repoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zstd = new DecompressionStream(source);
            using var dest = File.Create(destPath);
            zstd.CopyTo(dest);
        }
        else
        {
            File.Copy(repoFile, destPath, overwrite: true);
        }
    }

    /// <summary>序列化并写出清单文件。</summary>
    private static void WriteManifest(string path, BackupManifest manifest)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, ManifestJsonOptions));
    }

    /// <summary>读取并反序列化清单文件，文件损坏时返回空清单。</summary>
    private static BackupManifest LoadManifest(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BackupManifest>(json) ?? new BackupManifest();
    }

    /// <summary>从来源文本构建“清单前缀 → 来源根目录”映射，供还原到原始位置时解析绝对路径。</summary>
    private static Dictionary<string, string> BuildPrefixRootMap(string sourcesText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in Display.SplitList(sourcesText))
        {
            var source = raw.Trim();
            if (source.Length == 2 && char.IsLetter(source[0]) && source[1] == ':')
            {
                source += "\\";
            }

            string root;
            try
            {
                root = Path.GetFullPath(source);
            }
            catch (Exception)
            {
                continue;
            }

            var prefix = BuildPrefix(root, used);
            map[prefix] = root;
        }

        return map;
    }

    /// <summary>把清单相对路径解析为原始绝对路径（依赖来源前缀映射）。</summary>
    private static bool TryResolveOriginalPath(string relPath, IReadOnlyDictionary<string, string> prefixRoots, out string absolutePath)
    {
        absolutePath = "";
        var separator = relPath.IndexOf('\\');
        if (separator <= 0)
        {
            return false;
        }

        var prefix = relPath.Substring(0, separator);
        if (!prefixRoots.TryGetValue(prefix, out var root))
        {
            return false;
        }

        var rest = relPath.Substring(separator + 1);
        absolutePath = Path.Combine(root, rest);
        return true;
    }

    /// <summary>
    /// 当来源为 UNC 且主机对象配置了账号时，建立网络共享凭据会话；
    /// 否则返回 null（本地来源或使用当前登录身份）。
    /// </summary>
    private NetworkShare.NetworkSession? EstablishNetworkSessionIfNeeded(
        IReadOnlyDictionary<string, object?> job, string sourcesText)
    {
        var uncSource = Display.SplitList(sourcesText).FirstOrDefault(NetworkShare.IsUncPath);
        if (uncSource is null)
        {
            return null;
        }

        var userName = Row.Str(job, "host_username");
        var cipher = Row.Str(job, "host_password_cipher");
        if (string.IsNullOrWhiteSpace(userName) && string.IsNullOrWhiteSpace(cipher))
        {
            return null; // 未配置凭据，按当前进程身份访问
        }

        if (!_credentials.TryDecrypt(cipher, out var password, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return NetworkShare.Connect(uncSource, userName, password);
    }
}
