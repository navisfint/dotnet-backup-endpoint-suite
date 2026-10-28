using VaultGuard.Common.Data;
using VaultGuard.Common.Models;
using VaultGuard.Common.Security;

namespace VaultGuard.Backup.Web.Services;

/// <summary>
/// 备份还原系统数据仓储。集中封装“系统一”对 SQLite 库的全部读写，
/// 页面层与备份引擎都只通过本类访问数据库，保证 SQL 只写一处、便于审阅与登记。
/// </summary>
public sealed class BackupRepository
{
    private readonly Database _db;

    /// <summary>构造仓储。</summary>
    /// <param name="db">备份数据库（由依赖注入提供的单例）。</param>
    public BackupRepository(Database db)
    {
        _db = db;
    }

    // ============================================================
    //  管理用户与操作日志
    // ============================================================

    /// <summary>判断库中是否已存在管理用户（用于首次启动播种管理员）。</summary>
    public bool HasAnyUser()
        => Convert.ToInt64(_db.Scalar("SELECT COUNT(1) FROM users") ?? 0L) > 0;

    /// <summary>
    /// 新建管理用户。口令以 PBKDF2 加盐哈希保存，数据库中不出现明文。
    /// </summary>
    /// <param name="userName">登录用户名。</param>
    /// <param name="password">登录口令明文（仅在本方法内用于计算哈希）。</param>
    /// <param name="displayName">显示名称。</param>
    public void CreateUser(string userName, string password, string displayName)
    {
        var salt = PasswordHasher.NewSalt();
        var hash = PasswordHasher.Hash(password, salt);
        _db.Insert(
            "INSERT INTO users(username, password_hash, salt, display_name, created_at) " +
            "VALUES(@u, @h, @s, @d, @c)",
            new SqlArg("u", userName),
            new SqlArg("h", hash),
            new SqlArg("s", salt),
            new SqlArg("d", displayName),
            new SqlArg("c", Row.NowText()));
    }

    /// <summary>按用户名查询用户记录，不存在时返回 null。</summary>
    public Dictionary<string, object?>? FindUser(string userName)
        => _db.QueryOne("SELECT * FROM users WHERE username = @u LIMIT 1", new SqlArg("u", userName));

    /// <summary>写入一条操作日志（增删改、备份、还原等动作全部留痕）。</summary>
    public void LogOperation(string actor, string action, string target, string detail)
        => _db.Execute(
            "INSERT INTO op_logs(actor, action, target, detail, created_at) VALUES(@a, @c, @t, @d, @n)",
            new SqlArg("a", actor ?? ""),
            new SqlArg("c", action ?? ""),
            new SqlArg("t", target ?? ""),
            new SqlArg("d", detail ?? ""),
            new SqlArg("n", Row.NowText()));

    // ============================================================
    //  主机对象
    // ============================================================

    /// <summary>查询全部主机对象。</summary>
    public List<Dictionary<string, object?>> ListHosts()
        => _db.Query("SELECT * FROM hosts ORDER BY id DESC");

    /// <summary>按编号查询主机对象，不存在时返回 null。</summary>
    public Dictionary<string, object?>? GetHost(long hostId)
        => _db.QueryOne("SELECT * FROM hosts WHERE id = @id LIMIT 1", new SqlArg("id", hostId));

    /// <summary>查询全部主机（仅编号与名称），供任务页下列框使用。</summary>
    public List<Dictionary<string, object?>> ListHostOptions()
        => _db.Query("SELECT id, name, address FROM hosts ORDER BY name COLLATE NOCASE");

    /// <summary>新增主机对象，返回新主机编号。</summary>
    /// <param name="record">主机记录，密码字段应为已加密的密文。</param>
    public long InsertHost(HostRecord record)
        => _db.Insert(
            "INSERT INTO hosts(name, address, os_type, username, password_cipher, remark, " +
            "                  last_status, last_check_at, created_at) " +
            "VALUES(@n, @a, @o, @u, @p, @r, @s, NULL, @c)",
            new SqlArg("n", record.Name),
            new SqlArg("a", record.Address),
            new SqlArg("o", record.OsType),
            new SqlArg("u", record.UserName),
            new SqlArg("p", record.PasswordCipher),
            new SqlArg("r", record.Remark),
            new SqlArg("s", record.LastStatus),
            new SqlArg("c", Row.NowText()));

    /// <summary>编辑主机对象。密码为空串表示“保持原密码不变”。</summary>
    /// <param name="record">主机记录；<see cref="HostRecord.PasswordCipher"/> 为空则不更新密码列。</param>
    /// <param name="keepPassword">为 true 时保留数据库中的原密码密文。</param>
    public void UpdateHost(HostRecord record, bool keepPassword)
    {
        if (keepPassword)
        {
            _db.Execute(
                "UPDATE hosts SET name = @n, address = @a, os_type = @o, username = @u, remark = @r " +
                "WHERE id = @id",
                new SqlArg("n", record.Name),
                new SqlArg("a", record.Address),
                new SqlArg("o", record.OsType),
                new SqlArg("u", record.UserName),
                new SqlArg("r", record.Remark),
                new SqlArg("id", record.Id));
        }
        else
        {
            _db.Execute(
                "UPDATE hosts SET name = @n, address = @a, os_type = @o, username = @u, " +
                "                  password_cipher = @p, remark = @r WHERE id = @id",
                new SqlArg("n", record.Name),
                new SqlArg("a", record.Address),
                new SqlArg("o", record.OsType),
                new SqlArg("u", record.UserName),
                new SqlArg("p", record.PasswordCipher),
                new SqlArg("r", record.Remark),
                new SqlArg("id", record.Id));
        }
    }

    /// <summary>删除主机对象。存在关联备份任务时返回 false（拒绝删除，提示先处理任务）。</summary>
    public bool DeleteHost(long hostId)
    {
        var used = Convert.ToInt64(
            _db.Scalar("SELECT COUNT(1) FROM backup_jobs WHERE host_id = @id", new SqlArg("id", hostId)) ?? 0L);
        if (used > 0)
        {
            return false;
        }

        _db.Execute("DELETE FROM hosts WHERE id = @id", new SqlArg("id", hostId));
        return true;
    }

    /// <summary>回写主机连通性检测结果。</summary>
    /// <param name="hostId">主机编号。</param>
    /// <param name="status">检测结果文本（在线 / 离线）。</param>
    /// <param name="checkedAt">检测时间。</param>
    public void UpdateHostStatus(long hostId, string status, DateTime checkedAt)
        => _db.Execute(
            "UPDATE hosts SET last_status = @s, last_check_at = @t WHERE id = @id",
            new SqlArg("s", status),
            new SqlArg("t", Row.ToDbText(checkedAt)),
            new SqlArg("id", hostId));

    // ============================================================
    //  备份任务
    // ============================================================

    /// <summary>查询全部备份任务（左连接主机名称）。</summary>
    public List<Dictionary<string, object?>> ListJobs()
        => _db.Query(
            "SELECT j.id, j.name, j.host_id, j.sources, j.repo_path, j.mode, j.excludes, " +
            "       j.enabled, j.created_at, h.name AS host_name, h.address AS host_address " +
            "FROM backup_jobs j LEFT JOIN hosts h ON h.id = j.host_id " +
            "ORDER BY j.id DESC");

    /// <summary>按编号查询备份任务（含主机名称），不存在时返回 null。</summary>
    public Dictionary<string, object?>? GetJob(long jobId)
        => _db.QueryOne(
            "SELECT j.id, j.name, j.host_id, j.sources, j.repo_path, j.mode, j.excludes, " +
            "       j.enabled, j.created_at, h.name AS host_name, h.address AS host_address, " +
            "       h.username AS host_username, h.password_cipher AS host_password_cipher " +
            "FROM backup_jobs j LEFT JOIN hosts h ON h.id = j.host_id " +
            "WHERE j.id = @id LIMIT 1",
            new SqlArg("id", jobId));

    /// <summary>新增备份任务，返回新任务编号。</summary>
    public long InsertJob(BackupJobRecord record)
        => _db.Insert(
            "INSERT INTO backup_jobs(name, host_id, sources, repo_path, mode, excludes, enabled, created_at) " +
            "VALUES(@n, @h, @s, @r, @m, @e, @en, @c)",
            new SqlArg("n", record.Name),
            new SqlArg("h", record.HostId),
            new SqlArg("s", record.Sources),
            new SqlArg("r", record.RepoPath),
            new SqlArg("m", (int)record.Mode),
            new SqlArg("e", record.Excludes),
            new SqlArg("en", record.Enabled ? 1 : 0),
            new SqlArg("c", Row.NowText()));

    /// <summary>编辑备份任务。</summary>
    public void UpdateJob(BackupJobRecord record)
        => _db.Execute(
            "UPDATE backup_jobs SET name = @n, host_id = @h, sources = @s, repo_path = @r, " +
            "                      mode = @m, excludes = @e, enabled = @en WHERE id = @id",
            new SqlArg("n", record.Name),
            new SqlArg("h", record.HostId),
            new SqlArg("s", record.Sources),
            new SqlArg("r", record.RepoPath),
            new SqlArg("m", (int)record.Mode),
            new SqlArg("e", record.Excludes),
            new SqlArg("en", record.Enabled ? 1 : 0),
            new SqlArg("id", record.Id));

    /// <summary>删除备份任务。已有备份点时返回 false（不删除历史数据，避免误删）。</summary>
    public bool DeleteJob(long jobId)
    {
        var used = Convert.ToInt64(
            _db.Scalar("SELECT COUNT(1) FROM backup_points WHERE job_id = @id", new SqlArg("id", jobId)) ?? 0L);
        if (used > 0)
        {
            return false;
        }

        _db.Execute("DELETE FROM backup_jobs WHERE id = @id", new SqlArg("id", jobId));
        return true;
    }

    // ============================================================
    //  备份点
    // ============================================================

    /// <summary>写入一条“进行中”的备份点记录，返回备份点编号。</summary>
    /// <param name="jobId">所属任务编号。</param>
    /// <param name="jobName">任务名称快照。</param>
    /// <param name="hostName">主机名称快照。</param>
    /// <param name="mode">备份模式。</param>
    /// <param name="sources">来源快照。</param>
    /// <param name="startedAt">开始时间。</param>
    public long InsertRunningPoint(long jobId, string jobName, string hostName,
        BackupMode mode, string sources, DateTime startedAt)
        => _db.Insert(
            "INSERT INTO backup_points(job_id, job_name, host_name, mode, sources, started_at, status) " +
            "VALUES(@j, @jn, @hn, @m, @s, @t, '进行中')",
            new SqlArg("j", jobId),
            new SqlArg("jn", jobName),
            new SqlArg("hn", hostName),
            new SqlArg("m", (int)mode),
            new SqlArg("s", sources),
            new SqlArg("t", Row.ToDbText(startedAt)));

    /// <summary>备份成功后回填统计与清单路径。</summary>
    public void CompletePoint(long pointId, string manifestPath, int copiedCount, long copiedBytes,
        int totalCount, long totalBytes, DateTime finishedAt, double elapsedSeconds)
        => _db.Execute(
            "UPDATE backup_points SET status = '成功', manifest_path = @mp, copied_count = @cc, " +
            "       copied_bytes = @cb, total_count = @tc, total_bytes = @tb, finished_at = @f, " +
            "       elapsed_seconds = @e, message = '' WHERE id = @id",
            new SqlArg("mp", manifestPath),
            new SqlArg("cc", copiedCount),
            new SqlArg("cb", copiedBytes),
            new SqlArg("tc", totalCount),
            new SqlArg("tb", totalBytes),
            new SqlArg("f", Row.ToDbText(finishedAt)),
            new SqlArg("e", elapsedSeconds),
            new SqlArg("id", pointId));

    /// <summary>备份失败时回填失败状态与原因。</summary>
    public void FailPoint(long pointId, string message, DateTime finishedAt, double elapsedSeconds)
        => _db.Execute(
            "UPDATE backup_points SET status = '失败', message = @m, finished_at = @f, " +
            "       elapsed_seconds = @e WHERE id = @id",
            new SqlArg("m", message ?? ""),
            new SqlArg("f", Row.ToDbText(finishedAt)),
            new SqlArg("e", elapsedSeconds),
            new SqlArg("id", pointId));

    /// <summary>查询全部备份点（新到旧）。</summary>
    public List<Dictionary<string, object?>> ListPoints()
        => _db.Query(
            "SELECT id, job_id, job_name, host_name, mode, sources, started_at, finished_at, status, " +
            "       copied_count, copied_bytes, total_count, total_bytes, manifest_path, message, elapsed_seconds " +
            "FROM backup_points ORDER BY started_at DESC, id DESC");

    /// <summary>按编号查询备份点，不存在时返回 null。</summary>
    public Dictionary<string, object?>? GetPoint(long pointId)
        => _db.QueryOne(
            "SELECT id, job_id, job_name, host_name, mode, sources, started_at, finished_at, status, " +
            "       copied_count, copied_bytes, total_count, total_bytes, manifest_path, message, elapsed_seconds " +
            "FROM backup_points WHERE id = @id LIMIT 1",
            new SqlArg("id", pointId));

    /// <summary>取最近若干条备份点（首页“最近备份记录”）。</summary>
    public List<Dictionary<string, object?>> RecentPoints(int take)
        => _db.Query(
            "SELECT id, job_id, job_name, host_name, mode, started_at, finished_at, status, " +
            "       total_count, total_bytes, elapsed_seconds " +
            "FROM backup_points ORDER BY started_at DESC, id DESC LIMIT @n",
            new SqlArg("n", take));

    /// <summary>
    /// 查询同一任务最近一次“成功”的备份点，可按模式过滤。
    /// 增量备份（不限模式）取任意成功备份作为基线；差异备份仅取全量作为基线。
    /// </summary>
    /// <param name="jobId">任务编号。</param>
    /// <param name="mode">限定模式；null 表示不限模式。</param>
    /// <returns>备份点行（含 manifest_path），无匹配时返回 null。</returns>
    public Dictionary<string, object?>? FindLatestSuccessPoint(long jobId, BackupMode? mode)
    {
        if (mode.HasValue)
        {
            return _db.QueryOne(
                "SELECT id, mode, started_at, manifest_path FROM backup_points " +
                "WHERE job_id = @j AND status = '成功' AND mode = @m " +
                "ORDER BY started_at DESC, id DESC LIMIT 1",
                new SqlArg("j", jobId),
                new SqlArg("m", (int)mode.Value));
        }

        return _db.QueryOne(
            "SELECT id, mode, started_at, manifest_path FROM backup_points " +
            "WHERE job_id = @j AND status = '成功' " +
            "ORDER BY started_at DESC, id DESC LIMIT 1",
            new SqlArg("j", jobId));
    }

    // ============================================================
    //  还原记录
    // ============================================================

    /// <summary>写入一条文件还原记录。</summary>
    public void InsertRestore(RestoreRecord record)
        => _db.Execute(
            "INSERT INTO restore_records(point_id, files, target, file_count, total_bytes, operator, status, created_at) " +
            "VALUES(@p, @f, @t, @fc, @tb, @o, @s, @c)",
            new SqlArg("p", record.PointId),
            new SqlArg("f", record.Files),
            new SqlArg("t", record.Target),
            new SqlArg("fc", record.FileCount),
            new SqlArg("tb", record.TotalBytes),
            new SqlArg("o", record.Operator),
            new SqlArg("s", record.Status),
            new SqlArg("c", Row.NowText()));

    // ============================================================
    //  概览统计与日志查询
    // ============================================================

    /// <summary>统计主机总数。</summary>
    public long CountHosts() => Convert.ToInt64(_db.Scalar("SELECT COUNT(1) FROM hosts") ?? 0L);

    /// <summary>统计任务总数。</summary>
    public long CountJobs() => Convert.ToInt64(_db.Scalar("SELECT COUNT(1) FROM backup_jobs") ?? 0L);

    /// <summary>统计备份点总数。</summary>
    public long CountPoints() => Convert.ToInt64(_db.Scalar("SELECT COUNT(1) FROM backup_points") ?? 0L);

    /// <summary>取最近一次成功备份的时间文本，无记录时返回空串。</summary>
    public string LatestSuccessBackupTime()
        => Row.Str(_db.QueryOne(
            "SELECT finished_at FROM backup_points WHERE status = '成功' " +
            "ORDER BY finished_at DESC, id DESC LIMIT 1"), "finished_at");

    /// <summary>分页查询操作日志（新到旧）。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>当前页数据与总条数。</returns>
    public (List<Dictionary<string, object?>> Rows, int Total) QueryLogs(int page, int pageSize)
    {
        var total = Convert.ToInt32(_db.Scalar("SELECT COUNT(1) FROM op_logs") ?? 0);

        if (page < 1)
        {
            page = 1;
        }

        var rows = _db.Query(
            "SELECT id, actor, action, target, detail, created_at FROM op_logs " +
            "ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset",
            new SqlArg("limit", pageSize),
            new SqlArg("offset", (page - 1) * pageSize));

        return (rows, total);
    }
}
