using System.Globalization;
using VaultGuard.Common.Data;
using VaultGuard.Common.Security;

namespace VaultGuard.Endpoint.Server.Services;

/// <summary>
/// 终端安全数据仓储。集中封装"系统二"对 SQLite 库的全部读写，
/// 页面层与接口层都只通过本类访问数据库，保证 SQL 只写一处、便于审阅。
/// </summary>
public sealed class EndpointRepository
{
    private readonly Database _db;

    /// <summary>构造仓储。</summary>
    /// <param name="db">终端安全数据库（由依赖注入提供的单例）。</param>
    public EndpointRepository(Database db)
    {
        _db = db;
    }

    // ============================================================
    //  管理用户
    // ============================================================

    /// <summary>判断库中是否已存在管理用户（用于首次启动播种管理员）。</summary>
    public bool HasAnyUser()
        => Convert.ToInt64(_db.Scalar("SELECT COUNT(1) FROM users") ?? 0L) > 0;

    /// <summary>
    /// 新建管理用户。口令以 PBKDF2 加盐哈希保存，库中不出现明文。
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

    /// <summary>写入一条操作日志（登录、策略下发等管理动作留痕）。</summary>
    public void LogOperation(string actor, string action, string target, string detail)
        => _db.Execute(
            "INSERT INTO op_logs(actor, action, target, detail, created_at) VALUES(@a, @c, @t, @d, @n)",
            new SqlArg("a", actor ?? ""),
            new SqlArg("c", action ?? ""),
            new SqlArg("t", target ?? ""),
            new SqlArg("d", detail ?? ""),
            new SqlArg("n", Row.NowText()));

    // ============================================================
    //  终端对象
    // ============================================================

    /// <summary>心跳超时阈值（秒）。超过该时间没有新心跳，终端即视为离线。</summary>
    public const int OnlineThresholdSeconds = 90;

    /// <summary>根据最后心跳时间判断终端是否在线。</summary>
    public static bool IsOnline(IReadOnlyDictionary<string, object?> row)
    {
        var lastSeen = Row.Time(row, "last_seen_at");
        return lastSeen.HasValue
               && (DateTime.Now - lastSeen.Value).TotalSeconds <= OnlineThresholdSeconds;
    }

    /// <summary>
    /// 查询全部终端对象，并左连接其策略，供终端列表页一次取全。
    /// 不返回 <c>agent_token</c>，避免令牌出现在页面上。
    /// </summary>
    public List<Dictionary<string, object?>> ListAgents()
        => _db.Query(
            "SELECT a.id, a.hostname, a.ip_address, a.os_version, a.agent_version, a.status, " +
            "       a.allowed, a.last_seen_at, a.created_at, " +
            "       p.no_control_panel, p.disable_usb, p.audit_paths, p.updated_at, p.applied_at " +
            "FROM agents a " +
            "LEFT JOIN policies p ON p.agent_id = a.id " +
            "ORDER BY a.hostname COLLATE NOCASE");

    /// <summary>按终端编号查询单个终端（含策略），不存在时返回 null。</summary>
    public Dictionary<string, object?>? GetAgent(long agentId)
        => _db.QueryOne(
            "SELECT a.id, a.hostname, a.ip_address, a.os_version, a.agent_version, a.status, " +
            "       a.allowed, a.last_seen_at, a.created_at, " +
            "       p.no_control_panel, p.disable_usb, p.audit_paths, p.updated_at, p.applied_at " +
            "FROM agents a " +
            "LEFT JOIN policies p ON p.agent_id = a.id " +
            "WHERE a.id = @id LIMIT 1",
            new SqlArg("id", agentId));

    /// <summary>查询全部终端主机名（用于审计日志页的"计算机对象"下拉框）。</summary>
    public List<string> ListHostNames()
        => _db.Query("SELECT hostname FROM agents ORDER BY hostname COLLATE NOCASE")
              .Select(r => Row.Str(r, "hostname"))
              .Where(h => !string.IsNullOrWhiteSpace(h))
              .ToList();

    /// <summary>
    /// 终端注册：按计算机名做幂等处理。已存在的终端复用原令牌（终端重装后仍可续用），
    /// 新终端则生成令牌入库。
    /// </summary>
    /// <param name="hostName">终端计算机名。</param>
    /// <param name="ipAddress">终端 IP 地址。</param>
    /// <param name="osVersion">操作系统版本描述。</param>
    /// <param name="agentVersion">终端代理版本。</param>
    /// <returns>终端编号、通信令牌与是否允许接入。</returns>
    public (long AgentId, string Token, bool Allowed) RegisterAgent(
        string hostName, string ipAddress, string osVersion, string agentVersion)
    {
        var now = Row.NowText();
        var existing = _db.QueryOne("SELECT * FROM agents WHERE hostname = @h LIMIT 1", new SqlArg("h", hostName));

        if (existing is not null)
        {
            var agentId = Row.Long(existing, "id");
            var token = Row.Str(existing, "agent_token");

            // 历史数据可能没有令牌，这里补发一个
            if (string.IsNullOrWhiteSpace(token))
            {
                token = TokenGenerator.NewToken();
                _db.Execute("UPDATE agents SET agent_token = @t WHERE id = @id",
                    new SqlArg("t", token), new SqlArg("id", agentId));
            }

            _db.Execute(
                "UPDATE agents SET ip_address = @i, os_version = @o, agent_version = @v, " +
                "                  status = '在线', last_seen_at = @t WHERE id = @id",
                new SqlArg("i", ipAddress),
                new SqlArg("o", osVersion),
                new SqlArg("v", agentVersion),
                new SqlArg("t", now),
                new SqlArg("id", agentId));

            return (agentId, token, Row.Bool(existing, "allowed"));
        }

        var newToken = TokenGenerator.NewToken();
        var newId = _db.Insert(
            "INSERT INTO agents(hostname, ip_address, os_version, agent_token, agent_version, " +
            "                   status, allowed, last_seen_at, created_at) " +
            "VALUES(@h, @i, @o, @t, @v, '在线', 1, @n, @n)",
            new SqlArg("h", hostName),
            new SqlArg("i", ipAddress),
            new SqlArg("o", osVersion),
            new SqlArg("t", newToken),
            new SqlArg("v", agentVersion),
            new SqlArg("n", now));

        return (newId, newToken, true);
    }

    /// <summary>按通信令牌查找终端，令牌为空或不存在时返回 null。</summary>
    public Dictionary<string, object?>? FindAgentByToken(string token)
        => string.IsNullOrWhiteSpace(token)
            ? null
            : _db.QueryOne("SELECT * FROM agents WHERE agent_token = @t LIMIT 1", new SqlArg("t", token));

    /// <summary>
    /// 记录一次心跳：刷新在线状态、IP 与最后在线时间；
    /// 同时把该终端的策略标记为已投递（applied_at 回执）。
    /// </summary>
    public void MarkHeartbeat(long agentId, string ipAddress)
    {
        var now = Row.NowText();
        _db.Execute(
            "UPDATE agents SET last_seen_at = @t, status = '在线', ip_address = @i WHERE id = @id",
            new SqlArg("t", now),
            new SqlArg("i", ipAddress),
            new SqlArg("id", agentId));

        _db.Execute(
            "UPDATE policies SET applied_at = @t WHERE agent_id = @id",
            new SqlArg("t", now),
            new SqlArg("id", agentId));
    }

    /// <summary>把超时未上报心跳的终端标记为离线（控制台打开时顺带刷新）。</summary>
    /// <param name="offlineSeconds">超过该秒数未心跳即判定为离线。</param>
    public void MarkOfflineAgents(int offlineSeconds)
    {
        var threshold = Row.ToDbText(DateTime.Now.AddSeconds(-offlineSeconds));
        _db.Execute(
            "UPDATE agents SET status = '离线' " +
            "WHERE status <> '离线' AND (last_seen_at IS NULL OR last_seen_at < @t)",
            new SqlArg("t", threshold));
    }

    // ============================================================
    //  策略
    // ============================================================

    /// <summary>查询某终端的策略，未配置过时返回 null。</summary>
    public Dictionary<string, object?>? GetPolicy(long agentId)
        => _db.QueryOne("SELECT * FROM policies WHERE agent_id = @a LIMIT 1", new SqlArg("a", agentId));

    /// <summary>
    /// 保存（下发）某终端的策略。无记录则插入，有记录则更新，
    /// 并把 applied_at 置空表示"尚未被终端拉取"。
    /// </summary>
    /// <param name="agentId">终端编号。</param>
    /// <param name="noControlPanel">是否禁用控制面板。</param>
    /// <param name="disableUsb">是否禁用 USB 存储设备。</param>
    /// <param name="auditPaths">需要审计的目录集合。</param>
    public void SavePolicy(long agentId, bool noControlPanel, bool disableUsb, IEnumerable<string> auditPaths)
    {
        // 目录列表去掉空白项、按分号拼接后存入单列
        var joined = string.Join(";", auditPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim()));

        var now = Row.NowText();
        var exists = _db.Scalar("SELECT id FROM policies WHERE agent_id = @a", new SqlArg("a", agentId));

        if (exists is null)
        {
            _db.Insert(
                "INSERT INTO policies(agent_id, no_control_panel, disable_usb, audit_paths, updated_at, applied_at) " +
                "VALUES(@a, @n, @u, @p, @t, NULL)",
                new SqlArg("a", agentId),
                new SqlArg("n", noControlPanel ? 1 : 0),
                new SqlArg("u", disableUsb ? 1 : 0),
                new SqlArg("p", joined),
                new SqlArg("t", now));
        }
        else
        {
            _db.Execute(
                "UPDATE policies SET no_control_panel = @n, disable_usb = @u, audit_paths = @p, " +
                "                    updated_at = @t, applied_at = NULL WHERE agent_id = @a",
                new SqlArg("n", noControlPanel ? 1 : 0),
                new SqlArg("u", disableUsb ? 1 : 0),
                new SqlArg("p", joined),
                new SqlArg("t", now),
                new SqlArg("a", agentId));
        }
    }

    /// <summary>把分号/换行分隔的目录文本拆成目录数组。</summary>
    public static List<string> SplitPaths(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => p.Trim())
                  .Where(p => p.Length > 0)
                  .ToList();

    // ============================================================
    //  审计事件
    // ============================================================

    /// <summary>批量写入终端上报的文件操作审计事件，返回写入条数。</summary>
    /// <param name="agentId">终端编号。</param>
    /// <param name="hostName">终端计算机名快照。</param>
    /// <param name="events">事件集合。</param>
    public int InsertEvents(long agentId, string hostName, IEnumerable<AuditEventDto> events)
    {
        var receivedAt = Row.NowText();
        var count = 0;

        foreach (var item in events)
        {
            // 终端未提供发生时间或格式非法时，以控制端接收时间兜底，保证排序可用
            var occurredAt = Row.ToDbText(ParseOccurredAt(item.OccurredAt) ?? DateTime.Now);

            _db.Execute(
                "INSERT INTO audit_events(agent_id, hostname, event_type, path, target_path, " +
                "                         user_name, occurred_at, received_at) " +
                "VALUES(@a, @h, @e, @p, @tp, @u, @o, @r)",
                new SqlArg("a", agentId),
                new SqlArg("h", hostName),
                new SqlArg("e", item.EventType ?? ""),
                new SqlArg("p", item.Path ?? ""),
                new SqlArg("tp", item.TargetPath ?? ""),
                new SqlArg("u", item.UserName ?? ""),
                new SqlArg("o", occurredAt),
                new SqlArg("r", receivedAt));

            count++;
        }

        return count;
    }

    /// <summary>
    /// 解析终端上报的事件发生时间。兼容 ISO 8601 与 <c>yyyy-MM-dd HH:mm:ss</c> 两种写法，
    /// 无法解析时返回 null。
    /// </summary>
    private static DateTime? ParseOccurredAt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>取最近若干条审计事件（首页"最近事件"卡片使用）。</summary>
    public List<Dictionary<string, object?>> RecentEvents(int take)
        => _db.Query(
            "SELECT id, hostname, event_type, path, target_path, user_name, occurred_at " +
            "FROM audit_events ORDER BY occurred_at DESC, id DESC LIMIT @n",
            new SqlArg("n", take));

    /// <summary>
    /// 按事件类型统计指定时间之后发生的审计事件数量。
    /// </summary>
    /// <param name="since">统计起点（含）。</param>
    /// <returns>键为事件类型（新建/修改/删除/重命名）、值为数量的字典。</returns>
    public Dictionary<string, int> CountEventsByTypeSince(DateTime since)
    {
        var rows = _db.Query(
            "SELECT event_type, COUNT(1) AS cnt FROM audit_events WHERE occurred_at >= @t GROUP BY event_type",
            new SqlArg("t", Row.ToDbText(since)));

        return rows.ToDictionary(r => Row.Str(r, "event_type"), r => Row.Int(r, "cnt"));
    }

    /// <summary>
    /// 按"计算机对象 + 时间区间 + 事件类型 + 关键字"组合查询审计日志，并分页。
    /// </summary>
    /// <param name="hostName">计算机名，空表示不限。</param>
    /// <param name="eventType">事件类型，空表示不限。</param>
    /// <param name="keyword">路径关键字，空表示不限。</param>
    /// <param name="from">起始时间（含），null 表示不限。</param>
    /// <param name="to">结束时间（含），null 表示不限。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>当前页数据与满足条件的总条数。</returns>
    public (List<Dictionary<string, object?>> Rows, int Total) QueryEvents(
        string? hostName, string? eventType, string? keyword,
        DateTime? from, DateTime? to, int page, int pageSize)
    {
        var conditions = new List<string>();
        var args = new List<SqlArg>();

        if (!string.IsNullOrWhiteSpace(hostName))
        {
            conditions.Add("hostname = @host");
            args.Add(new SqlArg("host", hostName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            conditions.Add("event_type = @etype");
            args.Add(new SqlArg("etype", eventType.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add("(path LIKE @kw OR target_path LIKE @kw)");
            args.Add(new SqlArg("kw", "%" + keyword.Trim() + "%"));
        }

        if (from.HasValue)
        {
            conditions.Add("occurred_at >= @from");
            args.Add(new SqlArg("from", Row.ToDbText(from.Value)));
        }

        if (to.HasValue)
        {
            conditions.Add("occurred_at <= @to");
            args.Add(new SqlArg("to", Row.ToDbText(to.Value)));
        }

        var whereSql = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";

        var total = Convert.ToInt32(_db.Scalar("SELECT COUNT(1) FROM audit_events" + whereSql, args.ToArray()) ?? 0);

        if (page < 1)
        {
            page = 1;
        }

        var pagedArgs = new List<SqlArg>(args)
        {
            new("limit", pageSize),
            new("offset", (page - 1) * pageSize)
        };

        var rows = _db.Query(
            "SELECT id, agent_id, hostname, event_type, path, target_path, user_name, occurred_at, received_at " +
            "FROM audit_events" + whereSql + " ORDER BY occurred_at DESC, id DESC LIMIT @limit OFFSET @offset",
            pagedArgs.ToArray());

        return (rows, total);
    }
}
